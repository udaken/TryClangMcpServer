using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TryClangMcpServer.Constants;
using TryClangMcpServer.Models;
using TryClangMcpServer.Models.JsonRpc;
using TryClangMcpServer.Services;

namespace TryClangMcpServer.Controllers;

[ApiController]
[Route("mcp")]
[RequestSizeLimit(1_000_000)] // 1MB request size limit
public class McpController(
    IClangService clangService,
    IRateLimitingService rateLimitingService,
    ILogger<McpController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly IReadOnlyList<ToolDefinition> SupportedTools = new List<ToolDefinition>
    {
        new(JsonRpcConstants.CompileCppTool, "Compiles C/C++ code with various options and returns diagnostics",
            new
            {
                type = "object",
                properties = new
                {
                    sourceCode = new { type = "string", description = "The C/C++ source code to compile" },
                    options = new { type = "string", description = "Compiler options (optional)" }
                },
                required = new[] { "sourceCode" }
            }),
        new(JsonRpcConstants.AnalyzeCppTool, "Performs static analysis using Clang Static Analyzer",
            new
            {
                type = "object",
                properties = new
                {
                    sourceCode = new { type = "string", description = "The C/C++ source code to analyze" },
                    options = new { type = "string", description = "Analysis options (optional)" }
                },
                required = new[] { "sourceCode" }
            }),
        new(JsonRpcConstants.GetAstTool, "Generates Abstract Syntax Trees in JSON format",
            new
            {
                type = "object",
                properties = new
                {
                    sourceCode = new { type = "string", description = "The C/C++ source code to parse" },
                    options = new { type = "string", description = "Parser options (optional)" }
                },
                required = new[] { "sourceCode" }
            })
    };

    [HttpPost]
    public async ValueTask<IActionResult> HandleMcpRequest([FromBody] JsonElement request, CancellationToken cancellationToken = default)
    {
        var clientIp = GetClientIdentifier();

        try
        {
            // Rate limiting check
            if (!await rateLimitingService.IsAllowedAsync(clientIp))
            {
                return await HandleRateLimitExceeded(clientIp);
            }

            // Validate JSON-RPC structure
            var validationResult = ValidateJsonRpcRequest(request);
            if (!validationResult.IsValid)
            {
                logger.LogWarning("Invalid JSON-RPC request from client {ClientIp}: {Error}", clientIp, validationResult.Error);
                return BadRequest(CreateErrorResponse(GetRequestId(request), JsonRpcConstants.ErrorCodes.InvalidRequest, validationResult.Error!));
            }

            var requestId = GetRequestId(request);
            var method = request.GetProperty(JsonRpcConstants.Properties.Method).GetString()!;

            return method switch
            {
                JsonRpcConstants.ToolsListMethod => HandleToolsList(requestId),
                JsonRpcConstants.ToolsCallMethod => Ok(await HandleToolCall(request, clientIp, requestId, cancellationToken)),
                _ => HandleUnsupportedMethod(clientIp, method, requestId)
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid JSON in request from client {ClientIp}", clientIp);
            return BadRequest(CreateErrorResponse(1, JsonRpcConstants.ErrorCodes.ParseError, "Invalid JSON"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error processing request from client {ClientIp}: {Message}", clientIp, ex.Message);
            return StatusCode(500, CreateErrorResponse(GetRequestId(request), JsonRpcConstants.ErrorCodes.InternalError, "Internal server error"));
        }
    }

    private async ValueTask<IActionResult> HandleRateLimitExceeded(string clientIp)
    {
        var remainingQuota = await rateLimitingService.GetRemainingQuotaAsync(clientIp);
        logger.LogWarning("Rate limit exceeded for client {ClientIp}, remaining quota: {Quota}", clientIp, remainingQuota);

        return StatusCode(429, new JsonRpcResponse(
            JsonRpcConstants.Version,
            0,
            Error: new JsonRpcError(
                JsonRpcConstants.ErrorCodes.RateLimitExceeded,
                "Rate limit exceeded",
                new { remainingQuota })));
    }

    private IActionResult HandleToolsList(int requestId)
    {
        logger.LogDebug("Processing tools/list request");

        var response = new JsonRpcResponse(
            JsonRpcConstants.Version,
            requestId,
            Result: new
            {
                tools = SupportedTools.Select(tool => new
                {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = tool.InputSchema
                })
            });

        return Ok(response);
    }

    private IActionResult HandleUnsupportedMethod(string clientIp, string method, int requestId)
    {
        logger.LogWarning("Unsupported method requested by client {ClientIp}: {Method}", clientIp, method);
        return BadRequest(CreateErrorResponse(requestId, JsonRpcConstants.ErrorCodes.MethodNotFound, "Method not found"));
    }

    private string GetClientIdentifier()
    {
        // Try to get real client IP from headers (for reverse proxy scenarios)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            var firstIp = forwardedFor.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
            return realIp;

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static ValidationResult ValidateJsonRpcRequest(JsonElement request)
    {
        // Check for required JSON-RPC version
        if (!request.TryGetProperty(JsonRpcConstants.Properties.JsonRpc, out var versionElement))
            return ValidationResult.Failure($"Missing '{JsonRpcConstants.Properties.JsonRpc}' property");

        if (versionElement.GetString() != JsonRpcConstants.Version)
            return ValidationResult.Failure($"Invalid JSON-RPC version, must be '{JsonRpcConstants.Version}'");

        // Check for required method
        if (!request.TryGetProperty(JsonRpcConstants.Properties.Method, out var methodElement))
            return ValidationResult.Failure($"Missing '{JsonRpcConstants.Properties.Method}' property");

        var method = methodElement.GetString();
        if (string.IsNullOrEmpty(method))
            return ValidationResult.Failure("Method cannot be null or empty");

        // Validate method names
        if (method != JsonRpcConstants.ToolsListMethod && method != JsonRpcConstants.ToolsCallMethod)
            return ValidationResult.Failure($"Unsupported method: {method}");

        // For tools/call, validate params structure
        if (method == JsonRpcConstants.ToolsCallMethod)
        {
            if (!request.TryGetProperty(JsonRpcConstants.Properties.Params, out var paramsElement))
                return ValidationResult.Failure($"Missing '{JsonRpcConstants.Properties.Params}' property for {JsonRpcConstants.ToolsCallMethod}");

            if (!paramsElement.TryGetProperty(JsonRpcConstants.Properties.Name, out _))
                return ValidationResult.Failure($"Missing '{JsonRpcConstants.Properties.Name}' property in params");

            if (!paramsElement.TryGetProperty(JsonRpcConstants.Properties.Arguments, out _))
                return ValidationResult.Failure($"Missing '{JsonRpcConstants.Properties.Arguments}' property in params");
        }

        return ValidationResult.Success();
    }

    private static int GetRequestId(JsonElement request) =>
        request.TryGetProperty(JsonRpcConstants.Properties.Id, out var idProp) && idProp.TryGetInt32(out var id) ? id : 1;

    private async ValueTask<JsonRpcResponse> HandleToolCall(JsonElement request, string clientIp, int requestId, CancellationToken cancellationToken)
    {
        try
        {
            var paramsElement = request.GetProperty(JsonRpcConstants.Properties.Params);
            var toolName = paramsElement.GetProperty(JsonRpcConstants.Properties.Name).GetString();
            var arguments = paramsElement.GetProperty(JsonRpcConstants.Properties.Arguments);

            // Validate tool name
            if (string.IsNullOrEmpty(toolName))
            {
                return CreateErrorResponse(requestId, JsonRpcConstants.ErrorCodes.InvalidParams, "Invalid tool name");
            }

            // Validate arguments structure
            if (!arguments.TryGetProperty(JsonRpcConstants.Properties.SourceCode, out var sourceCodeElement))
            {
                return CreateErrorResponse(requestId, JsonRpcConstants.ErrorCodes.InvalidParams, $"Missing '{JsonRpcConstants.Properties.SourceCode}' argument");
            }

            var sourceCode = sourceCodeElement.GetString() ?? "";
            var options = arguments.TryGetProperty(JsonRpcConstants.Properties.Options, out var opts) ? opts.GetString() ?? "" : "";

            // Additional validation
            if (string.IsNullOrEmpty(sourceCode))
            {
                return CreateErrorResponse(requestId, JsonRpcConstants.ErrorCodes.InvalidParams, "Source code cannot be empty");
            }

            logger.LogDebug("Processing tool call '{ToolName}' from client {ClientIp}, source code length: {Length}",
                toolName, clientIp, sourceCode.Length);

            // Execute the tool using pattern matching
            var result = await ExecuteTool(toolName, sourceCode, options, cancellationToken);

            logger.LogDebug("Successfully processed tool call '{ToolName}' for client {ClientIp}", toolName, clientIp);

            return new JsonRpcResponse(
                JsonRpcConstants.Version,
                requestId,
                Result: new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = JsonSerializer.Serialize(result, JsonOptions)
                        }
                    }
                });
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "Missing required property in tool call from client {ClientIp}", clientIp);
            return CreateErrorResponse(requestId, JsonRpcConstants.ErrorCodes.InvalidParams, "Invalid params structure");
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid tool or arguments from client {ClientIp}: {Message}", clientIp, ex.Message);
            return CreateErrorResponse(requestId, JsonRpcConstants.ErrorCodes.InvalidParams, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing tool call from client {ClientIp}: {Message}", clientIp, ex.Message);
            return CreateErrorResponse(requestId, JsonRpcConstants.ErrorCodes.InternalError, "Tool execution failed");
        }
    }

    private async ValueTask<object> ExecuteTool(string toolName, string sourceCode, string options, CancellationToken cancellationToken) =>
        toolName switch
        {
            JsonRpcConstants.CompileCppTool => await clangService.CompileCppAsync(sourceCode, options),
            JsonRpcConstants.AnalyzeCppTool => await clangService.AnalyzeCppAsync(sourceCode, options),
            JsonRpcConstants.GetAstTool => await clangService.GetAstAsync(sourceCode, options),
            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };

    private static JsonRpcResponse CreateErrorResponse(int requestId, int errorCode, string message) =>
        new(JsonRpcConstants.Version, requestId, Error: new JsonRpcError(errorCode, message));
}