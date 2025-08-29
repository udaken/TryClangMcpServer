using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TryClangMcpServer.Models;
using TryClangMcpServer.Services;
using System.Collections.Concurrent;

namespace TryClangMcpServer.Controllers;

[ApiController]
[Route("mcp")]
[RequestSizeLimit(1_000_000)] // 1MB request size limit
public class McpController : ControllerBase
{
    private readonly IClangService _clangService;
    private readonly IRateLimitingService _rateLimitingService;
    private readonly ILogger<McpController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() 
    { 
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public McpController(IClangService clangService, IRateLimitingService rateLimitingService, ILogger<McpController> logger)
    {
        _clangService = clangService;
        _rateLimitingService = rateLimitingService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleMcpRequest([FromBody] JsonElement request)
    {
        var clientIp = GetClientIdentifier();
        
        try
        {
            // Rate limiting check
            if (!await _rateLimitingService.IsAllowedAsync(clientIp))
            {
                var remainingQuota = await _rateLimitingService.GetRemainingQuotaAsync(clientIp);
                _logger.LogWarning("Rate limit exceeded for client {ClientIp}, remaining quota: {Quota}", clientIp, remainingQuota);
                
                return StatusCode(429, new
                {
                    jsonrpc = "2.0",
                    error = new
                    {
                        code = -32099, // Custom rate limit error code
                        message = "Rate limit exceeded",
                        data = new { remainingQuota }
                    }
                });
            }

            // Validate JSON-RPC structure
            var validationResult = ValidateJsonRpcRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Invalid JSON-RPC request from client {ClientIp}: {Error}", clientIp, validationResult.Error);
                return BadRequest(new
                {
                    jsonrpc = "2.0",
                    error = new
                    {
                        code = -32600, // Invalid Request
                        message = validationResult.Error
                    }
                });
            }

            var requestId = GetRequestId(request);

            // Handle tools/list request
            if (request.TryGetProperty("method", out var methodProperty) &&
                methodProperty.GetString() == "tools/list")
            {
                _logger.LogDebug("Processing tools/list request from client {ClientIp}", clientIp);
                
                var toolsResponse = new
                {
                    jsonrpc = "2.0",
                    id = requestId,
                    result = new
                    {
                        tools = new[]
                        {
                            new
                            {
                                name = "compile_cpp",
                                description = "Compiles C/C++ code with various options and returns diagnostics",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        sourceCode = new { type = "string", description = "The C/C++ source code to compile" },
                                        options = new { type = "string", description = "Compiler options (optional)" }
                                    },
                                    required = new[] { "sourceCode" }
                                }
                            },
                            new
                            {
                                name = "analyze_cpp",
                                description = "Performs static analysis using Clang Static Analyzer",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        sourceCode = new { type = "string", description = "The C/C++ source code to analyze" },
                                        options = new { type = "string", description = "Analysis options (optional)" }
                                    },
                                    required = new[] { "sourceCode" }
                                }
                            },
                            new
                            {
                                name = "get_ast",
                                description = "Generates Abstract Syntax Trees in JSON format",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        sourceCode = new { type = "string", description = "The C/C++ source code to parse" },
                                        options = new { type = "string", description = "Parser options (optional)" }
                                    },
                                    required = new[] { "sourceCode" }
                                }
                            }
                        }
                    }
                };
                
                return Ok(toolsResponse);
            }
            
            // Handle tool calls
            if (request.TryGetProperty("method", out var toolMethod) &&
                toolMethod.GetString() == "tools/call")
            {
                var toolCallResult = await HandleToolCall(request, clientIp, requestId);
                return Ok(toolCallResult);
            }
            
            // Unsupported method
            _logger.LogWarning("Unsupported method requested by client {ClientIp}: {Method}", 
                clientIp, methodProperty.GetString() ?? "null");
                
            return BadRequest(new
            {
                jsonrpc = "2.0",
                id = requestId,
                error = new
                {
                    code = -32601, // Method not found
                    message = "Method not found"
                }
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in request from client {ClientIp}", clientIp);
            return BadRequest(new
            {
                jsonrpc = "2.0",
                error = new
                {
                    code = -32700, // Parse error
                    message = "Invalid JSON"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing request from client {ClientIp}: {Message}", clientIp, ex.Message);
            return StatusCode(500, new
            {
                jsonrpc = "2.0",
                error = new
                {
                    code = -32603, // Internal error
                    message = "Internal server error"
                }
            });
        }
    }

    private string GetClientIdentifier()
    {
        // Try to get real client IP from headers (for reverse proxy scenarios)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            var firstIp = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
            return realIp;

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static (bool IsValid, string? Error) ValidateJsonRpcRequest(JsonElement request)
    {
        // Check for required JSON-RPC version
        if (!request.TryGetProperty("jsonrpc", out var versionElement))
            return (false, "Missing 'jsonrpc' property");

        if (versionElement.GetString() != "2.0")
            return (false, "Invalid JSON-RPC version, must be '2.0'");

        // Check for required method
        if (!request.TryGetProperty("method", out var methodElement))
            return (false, "Missing 'method' property");

        var method = methodElement.GetString();
        if (string.IsNullOrEmpty(method))
            return (false, "Method cannot be null or empty");

        // Validate method names
        if (method != "tools/list" && method != "tools/call")
            return (false, $"Unsupported method: {method}");

        // For tools/call, validate params structure
        if (method == "tools/call")
        {
            if (!request.TryGetProperty("params", out var paramsElement))
                return (false, "Missing 'params' property for tools/call");

            if (!paramsElement.TryGetProperty("name", out _))
                return (false, "Missing 'name' property in params");

            if (!paramsElement.TryGetProperty("arguments", out _))
                return (false, "Missing 'arguments' property in params");
        }

        return (true, null);
    }

    private static int GetRequestId(JsonElement request)
    {
        return request.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out var id) 
            ? id : 1;
    }

    private async Task<object> HandleToolCall(JsonElement request, string clientIp, int requestId)
    {
        try
        {
            var paramsElement = request.GetProperty("params");
            var toolName = paramsElement.GetProperty("name").GetString();
            var arguments = paramsElement.GetProperty("arguments");

            // Validate tool name
            if (string.IsNullOrEmpty(toolName))
            {
                return CreateErrorResponse(requestId, -32602, "Invalid tool name");
            }

            // Validate arguments structure
            if (!arguments.TryGetProperty("sourceCode", out var sourceCodeElement))
            {
                return CreateErrorResponse(requestId, -32602, "Missing 'sourceCode' argument");
            }

            var sourceCode = sourceCodeElement.GetString() ?? "";
            var options = arguments.TryGetProperty("options", out var opts) ? opts.GetString() ?? "" : "";

            // Additional validation
            if (string.IsNullOrEmpty(sourceCode))
            {
                return CreateErrorResponse(requestId, -32602, "Source code cannot be empty");
            }

            _logger.LogDebug("Processing tool call '{ToolName}' from client {ClientIp}, source code length: {Length}", 
                toolName, clientIp, sourceCode.Length);

            // Execute the tool
            object result = toolName switch
            {
                "compile_cpp" => await _clangService.CompileCppAsync(sourceCode, options),
                "analyze_cpp" => await _clangService.AnalyzeCppAsync(sourceCode, options),
                "get_ast" => await _clangService.GetAstAsync(sourceCode, options),
                _ => throw new ArgumentException($"Unknown tool: {toolName}")
            };

            var toolResponse = new
            {
                jsonrpc = "2.0",
                id = requestId,
                result = new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = JsonSerializer.Serialize(result, JsonOptions)
                        }
                    }
                }
            };

            _logger.LogDebug("Successfully processed tool call '{ToolName}' for client {ClientIp}", toolName, clientIp);
            return toolResponse;
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Missing required property in tool call from client {ClientIp}", clientIp);
            return CreateErrorResponse(requestId, -32602, "Invalid params structure");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid tool or arguments from client {ClientIp}: {Message}", clientIp, ex.Message);
            return CreateErrorResponse(requestId, -32602, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool call from client {ClientIp}: {Message}", clientIp, ex.Message);
            return CreateErrorResponse(requestId, -32603, "Tool execution failed");
        }
    }

    private static object CreateErrorResponse(int requestId, int errorCode, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            id = requestId,
            error = new
            {
                code = errorCode,
                message
            }
        };
    }
}