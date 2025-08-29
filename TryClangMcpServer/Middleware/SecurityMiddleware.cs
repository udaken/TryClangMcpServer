using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using TryClangMcpServer.Constants;

namespace TryClangMcpServer.Middleware;

/// <summary>
/// Security middleware for additional request validation and protection
/// </summary>
public class SecurityMiddleware(RequestDelegate next, ILogger<SecurityMiddleware> logger)
{
    private static readonly FrozenSet<string> AllowedContentTypes = new[]
    {
        "application/json",
        "application/json; charset=utf-8",
        "text/json"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> SuspiciousPatterns = new[]
    {
        "<script", "javascript:", "data:", "vbscript:",
        "..", "//", "\\", "%00", "%2e%2e"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> SuspiciousUserAgentPatterns = new[]
    {
        "sqlmap", "nikto", "nmap", "masscan", "zap", "burp",
        "python-requests", "curl/", "wget/", "libwww-perl",
        "bot", "crawler", "spider", "scraper"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public async ValueTask InvokeAsync(HttpContext context)
    {
        // Only apply security checks to MCP endpoints
        if (context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
        {
            var validationResult = ValidateRequest(context);
            if (!validationResult.IsValid)
            {
                await WriteSecurityErrorAsync(context, validationResult.ErrorCode, validationResult.ErrorMessage);
                return;
            }
        }

        await next(context);
    }

    private ValidationResult ValidateRequest(HttpContext context)
    {
        var request = context.Request;
        var clientIp = GetClientIP(context);

        // 1. Method validation
        if (!HttpMethods.IsPost(request.Method))
        {
            logger.LogWarning("Invalid HTTP method {Method} from client {ClientIp}", request.Method, clientIp);
            return new ValidationResult(false, 405, "Method not allowed");
        }

        // 2. Content-Type validation
        var contentType = request.ContentType;
        if (string.IsNullOrEmpty(contentType) ||
            !AllowedContentTypes.Any(allowed => contentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("Invalid content type {ContentType} from client {ClientIp}", contentType ?? "null", clientIp);
            return new ValidationResult(false, 415, "Unsupported media type");
        }

        // 3. Content-Length validation (double-check beyond RequestSizeLimit)
        if (request.ContentLength > 1_000_000)
        {
            logger.LogWarning("Request too large {Size} bytes from client {ClientIp}", request.ContentLength, clientIp);
            return new ValidationResult(false, 413, "Request entity too large");
        }

        // 4. Basic header validation
        if (request.Headers.TryGetValue("X-Forwarded-Host", out var forwardedHost) &&
            !IsValidForwardedHost(forwardedHost.ToString()))
        {
            logger.LogWarning("Suspicious forwarded host header from client {ClientIp}", clientIp);
            return new ValidationResult(false, 400, "Invalid forwarded host");
        }

        // 5. User-Agent validation (log suspicious agents but don't block)
        var userAgent = request.Headers.UserAgent.ToString();
        if (string.IsNullOrEmpty(userAgent) || IsSuspiciousUserAgent(userAgent))
        {
            logger.LogDebug("Suspicious or missing user agent from client {ClientIp}: {UserAgent}", clientIp, userAgent);
        }

        return new ValidationResult(true, 0, string.Empty);
    }

    private static string GetClientIP(HttpContext context) =>
        context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault() ??
        context.Request.Headers["X-Real-IP"].FirstOrDefault() ??
        context.Connection.RemoteIpAddress?.ToString() ??
        "unknown";

    private static bool IsValidForwardedHost(string forwardedHost) =>
        string.IsNullOrWhiteSpace(forwardedHost) ||
        !SuspiciousPatterns.Any(pattern => forwardedHost.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static bool IsSuspiciousUserAgent(string userAgent) =>
        !string.IsNullOrWhiteSpace(userAgent) &&
        SuspiciousUserAgentPatterns.Any(pattern => userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private async ValueTask WriteSecurityErrorAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            jsonrpc = JsonRpcConstants.Version,
            error = new
            {
                code = -32000, // Server error range
                message
            }
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json, Encoding.UTF8);
    }

    private readonly record struct ValidationResult(bool IsValid, int ErrorCode, string ErrorMessage);
}