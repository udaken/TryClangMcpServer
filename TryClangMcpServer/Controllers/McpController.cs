using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TryClangMcpServer.Models;
using TryClangMcpServer.Services;

namespace TryClangMcpServer.Controllers;

[ApiController]
[Route("mcp")]
public class McpController : ControllerBase
{
    private readonly IClangService _clangService;
    private readonly ILogger<McpController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() 
    { 
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public McpController(IClangService clangService, ILogger<McpController> logger)
    {
        _clangService = clangService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleMcpRequest([FromBody] JsonElement request)
    {
        try
        {
            if (request.TryGetProperty("method", out var methodProperty) &&
                methodProperty.GetString() == "tools/list")
            {
                var toolsResponse = new
                {
                    jsonrpc = "2.0",
                    id = request.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 1,
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
                var paramsElement = request.GetProperty("params");
                var toolName = paramsElement.GetProperty("name").GetString();
                var arguments = paramsElement.GetProperty("arguments");
                
                var sourceCode = arguments.GetProperty("sourceCode").GetString() ?? "";
                var options = arguments.TryGetProperty("options", out var opts) ? opts.GetString() ?? "" : "";
                
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
                    id = request.TryGetProperty("id", out var idProp2) ? idProp2.GetInt32() : 1,
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
                
                return Ok(toolResponse);
            }
            
            // Default error response
            return BadRequest(new { error = "Unsupported method" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP request processing error: {Message}", ex.Message);
            return StatusCode(500, new { error = "Internal server error occurred" });
        }
    }
}