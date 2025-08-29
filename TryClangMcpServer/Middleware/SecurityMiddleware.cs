using System.Text;
using System.Text.Json;

namespace TryClangMcpServer.Middleware;

/// <summary>
/// Security middleware for additional request validation and protection
/// </summary>
public class SecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityMiddleware> _logger;
    
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/json; charset=utf-8",
        "text/json"
    };

    public SecurityMiddleware(RequestDelegate next, ILogger<SecurityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
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

        await _next(context);
    }

    private (bool IsValid, int ErrorCode, string ErrorMessage) ValidateRequest(HttpContext context)
    {
        var request = context.Request;
        var clientIp = GetClientIP(context);

        // 1. Method validation
        if (request.Method != HttpMethods.Post)
        {
            _logger.LogWarning("Invalid HTTP method {Method} from client {ClientIp}", request.Method, clientIp);
            return (false, 405, "Method not allowed");
        }

        // 2. Content-Type validation
        var contentType = request.ContentType;
        if (string.IsNullOrEmpty(contentType) || 
            !AllowedContentTypes.Any(allowed => contentType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Invalid content type {ContentType} from client {ClientIp}", contentType ?? "null", clientIp);
            return (false, 415, "Unsupported media type");
        }

        // 3. Content-Length validation (already handled by RequestSizeLimit attribute, but double-check)
        if (request.ContentLength > 1_000_000)
        {
            _logger.LogWarning("Request too large {Size} bytes from client {ClientIp}", request.ContentLength, clientIp);
            return (false, 413, "Request entity too large");
        }

        // 4. Basic header validation
        if (request.Headers.ContainsKey("X-Forwarded-Host") && 
            !IsValidForwardedHost(request.Headers["X-Forwarded-Host"].ToString()))
        {
            _logger.LogWarning("Suspicious forwarded host header from client {ClientIp}", clientIp);
            return (false, 400, "Invalid forwarded host");
        }

        // 5. User-Agent validation (optional, but helps detect some automated attacks)
        var userAgent = request.Headers.UserAgent.ToString();
        if (string.IsNullOrEmpty(userAgent) || IsSuspiciousUserAgent(userAgent))
        {
            _logger.LogDebug("Suspicious or missing user agent from client {ClientIp}: {UserAgent}", clientIp, userAgent);
            // Don't block, just log for monitoring
        }

        return (true, 0, string.Empty);
    }

    private static string GetClientIP(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').FirstOrDefault()?.Trim() ?? "unknown";
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
            return realIp;

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static bool IsValidForwardedHost(string forwardedHost)
    {
        if (string.IsNullOrWhiteSpace(forwardedHost))
            return true;

        // Basic validation - should not contain suspicious patterns
        var suspiciousPatterns = new[]
        {
            "<script", "javascript:", "data:", "vbscript:",
            "..", "//", "\\", "%00", "%2e%2e"
        };

        return !suspiciousPatterns.Any(pattern => 
            forwardedHost.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSuspiciousUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return false;

        // Common patterns of automated tools/bots that might indicate attacks
        var suspiciousPatterns = new[]
        {
            "sqlmap", "nikto", "nmap", "masscan", "zap", "burp",
            "python-requests", "curl/", "wget/", "libwww-perl",
            "bot", "crawler", "spider", "scraper"
        };

        return suspiciousPatterns.Any(pattern => 
            userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private async Task WriteSecurityErrorAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            jsonrpc = "2.0",
            error = new
            {
                code = -32000, // Server error range
                message = message
            }
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        });

        await context.Response.WriteAsync(json, Encoding.UTF8);
    }
}