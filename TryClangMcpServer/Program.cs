using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ClangSharp;
using ClangSharp.Interop;

// Check if HTTP mode is requested via command line argument
var useHttpMode = args.Contains("--http");

// Shared JSON options
var JsonOptions = new JsonSerializerOptions 
{ 
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

if (useHttpMode)
{
    // HTTP mode configuration
    var builder = WebApplication.CreateBuilder(args);
    
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Information;
    });
    
    // Add CORS
    builder.Services.AddCors();
    
    // Add controllers for HTTP endpoints
    builder.Services.AddControllers();
    
    // Add MCP server services
    builder.Services
        .AddMcpServer()
        .WithToolsFromAssembly();
    
    var app = builder.Build();
    
    // Configure CORS with environment-aware security
    app.UseCors(policy =>
    {
        var isDevelopment = app.Environment.IsDevelopment();
        
        if (isDevelopment)
        {
            // Development: Allow localhost only
            policy.WithOrigins("http://localhost:*", "https://localhost:*", 
                              "http://127.0.0.1:*", "https://127.0.0.1:*")
                  .SetIsOriginAllowedToAllowWildcardSubdomains()
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // Production: Strict CORS policy
            policy.WithOrigins() // No origins allowed by default
                  .WithMethods("POST") // Only POST for MCP
                  .WithHeaders("Content-Type", "Accept")
                  .DisallowCredentials();
        }
    });
    
    // Add health check endpoint
    app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });
    
    // Add MCP endpoint for tools listing
    app.MapPost("/mcp", async (HttpContext context) =>
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var requestBody = await reader.ReadToEndAsync();
            var jsonDoc = JsonDocument.Parse(requestBody);
            
            if (jsonDoc.RootElement.TryGetProperty("method", out var methodProperty) &&
                methodProperty.GetString() == "tools/list")
            {
                var toolsResponse = new
                {
                    jsonrpc = "2.0",
                    id = jsonDoc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 1,
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
                                description = "Generates Abstract Syntax Trees in multiple formats",
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
                
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(toolsResponse, JsonOptions));
                return;
            }
            
            // Handle tool calls
            if (jsonDoc.RootElement.TryGetProperty("method", out var toolMethod) &&
                toolMethod.GetString() == "tools/call")
            {
                var paramsElement = jsonDoc.RootElement.GetProperty("params");
                var toolName = paramsElement.GetProperty("name").GetString();
                var arguments = paramsElement.GetProperty("arguments");
                
                string result = toolName switch
                {
                    "compile_cpp" => await Clang.CompileCpp(
                        arguments.GetProperty("sourceCode").GetString() ?? "",
                        arguments.TryGetProperty("options", out var opts) ? opts.GetString() ?? "" : ""),
                    "analyze_cpp" => await Clang.AnalyzeCpp(
                        arguments.GetProperty("sourceCode").GetString() ?? "",
                        arguments.TryGetProperty("options", out var opts2) ? opts2.GetString() ?? "" : ""),
                    "get_ast" => await Clang.GetAst(
                        arguments.GetProperty("sourceCode").GetString() ?? "",
                        arguments.TryGetProperty("options", out var opts3) ? opts3.GetString() ?? "" : ""),
                    _ => throw new ArgumentException($"Unknown tool: {toolName}")
                };
                
                var toolResponse = new
                {
                    jsonrpc = "2.0",
                    id = jsonDoc.RootElement.TryGetProperty("id", out var idProp2) ? idProp2.GetInt32() : 1,
                    result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = result
                            }
                        }
                    }
                };
                
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(toolResponse, JsonOptions));
                return;
            }
            
            // Default error response
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("{\"error\":\"Unsupported method\"}");
        }
        catch (Exception ex)
        {
            var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<object>();
            logger.LogError(ex, "HTTP request processing error: {Message}", ex.Message);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Internal server error occurred" }, JsonOptions));
        }
    });
    
    var port = GetPortFromArgs(args) ?? 3000;
    app.Urls.Add($"http://localhost:{port}");
    
    Console.WriteLine($"🚀 MCP Server running in HTTP mode on http://localhost:{port}");
    Console.WriteLine("📋 Available endpoints:");
    Console.WriteLine($"  • Health: GET http://localhost:{port}/health");
    Console.WriteLine($"  • Tools: POST http://localhost:{port}/mcp");
    Console.WriteLine("📝 Example requests:");
    Console.WriteLine("  • List tools: POST /mcp with {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
    Console.WriteLine("  • Call tool: POST /mcp with {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"compile_cpp\",\"arguments\":{\"sourceCode\":\"int main(){return 0;}\"}}}");
    
    await app.RunAsync();
}
else
{
    // Stdio mode configuration (default)
    var builder = Host.CreateApplicationBuilder(args);
    
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        // Configure all logs to go to stderr for stdio mode
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
        
    await builder.Build().RunAsync();
}

static int? GetPortFromArgs(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--port" && int.TryParse(args[i + 1], out var port))
        {
            return port;
        }
    }
    return null;
}

/// <summary>
/// Configuration constants for Clang operations
/// </summary>
public static class ClangConstants
{
    public const int MaxAstDepth = 10;
    public const string DefaultSourceFileName = "source.cpp";
    public const int CleanupRetryAttempts = 3;
    public const int CleanupDelayMs = 100;
    public const int MaxSourceCodeSizeBytes = 1_000_000; // 1MB
    
    // Security and resource limits
    public const int MaxConcurrentOperations = 5;
    public const int OperationTimeoutMs = 30_000; // 30 seconds
    public const int RateLimitRequestsPerMinute = 60;
    public const int MaxRequestsPerHour = 1000;
    
    public static readonly string[] DangerousOptions = 
    {
        // File system access
        "-o", "--output", "-include", "-I", "--include-directory",
        "--sysroot", "-isysroot", "-working-directory",
        
        // System information leakage
        "-march=native", "-mcpu=native", "-mtune=native",
        "-pipe", "-v", "--verbose",
        
        // Preprocessor and dependency generation (can reveal system info)
        "-M", "-MD", "-MM", "-MF", "-MP", "-MT", "-MQ", "-MMD",
        
        // Temporary file handling
        "-save-temps", "--save-temps", "-save-temps=",
        
        // Target and architecture specification
        "-target", "--target", "-mfloat-abi", "-mfpu",
        
        // External tools and scripts
        "-x", "--language", "-Xclang", "-Xpreprocessor", "-Xlinker", "-Xassembler",
        
        // Debug and profiling (can leak info)
        "-g", "-gdwarf", "-glldb", "-gsce", "-gcodeview",
        "-pg", "--coverage", "-fprofile",
        
        // Linker options
        "-l", "-L", "--library", "--library-path",
        "-Wl,", "-Xlinker",
        
        // Plugin loading
        "-fplugin", "-load",
        
        // External command execution
        "-B", "--prefix", "-specs",
        
        // Linux-specific dangerous options
        "-rpath", "--rpath", "-soname", "--soname",
        "-shared", "--shared", "-static", "--static",
        "-pie", "-no-pie", "-fpic", "-fPIC", "-fpie", "-fPIE",
        "-rdynamic", "--export-dynamic",
        
        // GCC/Linux specific system access
        "-print-prog-name", "-print-file-name", "-print-libgcc-file-name",
        "-print-search-dirs", "-print-multi-directory", "-print-multi-lib",
        "-print-sysroot", "-print-sysroot-headers-suffix"
    };
}

/// <summary>
/// Represents the type of Clang operation to perform
/// </summary>
public enum ClangOperation
{
    Compile,
    Analyze, 
    GenerateAST
}

/// <summary>
/// Represents an AST node with strongly-typed properties
/// </summary>
public record AstNode(string Kind, string Spelling, uint Line, uint Column)
{
    public List<AstNode> Children { get; init; } = new();
}

[McpServerToolType]
public static class Clang
{
    private static ILogger? _logger;
    private static ILogger Logger => _logger ??= 
        LoggerFactory.Create(b => b.AddConsole()).CreateLogger<object>();

    private static readonly JsonSerializerOptions JsonOptions = new() 
    { 
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    // Semaphore to limit concurrent operations
    private static readonly SemaphoreSlim ConcurrencyLimiter = 
        new(ClangConstants.MaxConcurrentOperations, ClangConstants.MaxConcurrentOperations);

    [McpServerTool, Description("Compiles C/C++ code with various options and returns diagnostics")]
    public static async Task<string> CompileCpp(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic")
    {
        return await ExecuteClangOperationAsync(sourceCode, options, ClangOperation.Compile, "compilation");
    }

    [McpServerTool, Description("Performs static analysis using Clang Static Analyzer.")]
    public static async Task<string> AnalyzeCpp(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic")
    {
        return await ExecuteClangOperationAsync(sourceCode, options, ClangOperation.Analyze, "analysis");
    }
    
    [McpServerTool, Description("Generates Abstract Syntax Trees in multiple formats.")]
    public static async Task<string> GetAst(string sourceCode, string options = "")
    {
        return await ExecuteClangOperationAsync(sourceCode, options, ClangOperation.GenerateAST, "AST generation");
    }

    /// <summary>
    /// Executes a Clang operation with proper resource management and error handling
    /// </summary>
    private static async Task<string> ExecuteClangOperationAsync(
        string sourceCode, 
        string options, 
        ClangOperation operation,
        string operationName)
    {
        // Wait for available slot (with timeout)
        using var timeoutCts = new CancellationTokenSource(ClangConstants.OperationTimeoutMs);
        
        if (!await ConcurrencyLimiter.WaitAsync(5000, timeoutCts.Token)) // 5 second wait for slot
        {
            Logger.LogWarning("Operation {Operation} rejected due to concurrency limit", operationName);
            return CreateErrorResult($"{operationName} temporarily unavailable due to high load");
        }
        
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        try
        {
            // Validate inputs
            ValidateSourceCode(sourceCode);
            var args = ValidateAndParseOptions(options);

            // Setup temporary directory and file
            Directory.CreateDirectory(tempDir);
            var sourceFile = Path.Combine(tempDir, ClangConstants.DefaultSourceFileName);
            await File.WriteAllTextAsync(sourceFile, sourceCode, Encoding.UTF8);
            
            args = [.. args, "-c"];

            // Prepare arguments based on operation
            args = operation == ClangOperation.Analyze 
                ? [sourceFile, .. args, "--analyze"] 
                : [sourceFile, .. args];
            
            // Execute Clang operation with proper resource management
            using var index = CXIndex.Create();
            
            Logger.LogDebug("Creating translation unit for {SourceFile} with args: [{Args}]", 
                sourceFile, string.Join(", ", args));
            
            using var translationUnit = CXTranslationUnit.Parse(
                index, sourceFile, args, [], 
                CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord);
            
            if (translationUnit.Handle == IntPtr.Zero)
            {
                Logger.LogError("Failed to create translation unit for {Operation}. SourceFile: {SourceFile}, Args: [{Args}]", 
                    operationName, sourceFile, string.Join(", ", args));
                
                // Try with minimal arguments for basic C++
                var basicArgs = new[] { "-std=c++17", "-x", "c++" };
                Logger.LogDebug("Attempting fallback with basic args: [{BasicArgs}]", string.Join(", ", basicArgs));
                
                using var fallbackTU = CXTranslationUnit.Parse(
                    index, sourceFile, basicArgs, [], CXTranslationUnit_Flags.CXTranslationUnit_None);
                    
                if (fallbackTU.Handle == IntPtr.Zero)
                {
                    return CreateErrorResult($"Failed to create translation unit for {operationName} (both normal and fallback attempts failed)");
                }
                
                // Use the fallback translation unit
                var fallbackResult = operation switch
                {
                    ClangOperation.Compile => CreateCompilationResult(fallbackTU),
                    ClangOperation.Analyze => CreateAnalysisResult(fallbackTU),
                    ClangOperation.GenerateAST => await CreateAstResultAsync(fallbackTU),
                    _ => throw new ArgumentException($"Unknown operation: {operation}", nameof(operation))
                };
                
                Logger.LogWarning("{Operation} completed using fallback method", operationName);
                return JsonSerializer.Serialize(fallbackResult, JsonOptions);
            }
            
            // Generate result based on operation type
            var result = operation switch
            {
                ClangOperation.Compile => CreateCompilationResult(translationUnit),
                ClangOperation.Analyze => CreateAnalysisResult(translationUnit),
                ClangOperation.GenerateAST => await CreateAstResultAsync(translationUnit),
                _ => throw new ArgumentException($"Unknown operation: {operation}", nameof(operation))
            };
            
            Logger.LogDebug("{Operation} completed successfully", operationName);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            Logger.LogWarning("Operation {Operation} timed out after {Timeout}ms", operationName, ClangConstants.OperationTimeoutMs);
            return CreateErrorResult($"{operationName} operation timed out");
        }
        catch (ArgumentException ex)
        {
            Logger.LogWarning(ex, "Invalid input for {Operation}: {Message}", operationName, ex.Message);
            return CreateErrorResult($"Invalid input provided for {operationName}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during {Operation}: {Message}", operationName, ex.Message);
            return CreateErrorResult($"{operationName} operation failed due to an internal error");
        }
        finally
        {
            // Always release the semaphore and cleanup
            ConcurrencyLimiter.Release();
            await CleanupDirectoryAsync(tempDir);
        }
    }

    /// <summary>
    /// Validates source code input
    /// </summary>
    private static void ValidateSourceCode(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new ArgumentException("Source code cannot be null or empty", nameof(sourceCode));
        
        if (Encoding.UTF8.GetByteCount(sourceCode) > ClangConstants.MaxSourceCodeSizeBytes)
            throw new ArgumentException($"Source code too large (max {ClangConstants.MaxSourceCodeSizeBytes / 1024}KB)", nameof(sourceCode));
    }

    /// <summary>
    /// Validates and parses compiler options, filtering out dangerous ones
    /// </summary>
    private static string[] ValidateAndParseOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return Array.Empty<string>();
            
        var parsed = options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Check for dangerous options with more precise matching
        var dangerousOption = parsed.FirstOrDefault(opt => 
            ClangConstants.DangerousOptions.Any(dangerous => 
            {
                // Exact match
                if (opt.Equals(dangerous, StringComparison.OrdinalIgnoreCase))
                    return true;
                
                // Match options with = (like -save-temps=dir)
                if (dangerous.EndsWith("=") && opt.StartsWith(dangerous, StringComparison.OrdinalIgnoreCase))
                    return true;
                
                // Match options followed by = (like -o=file)
                if (opt.StartsWith(dangerous + "=", StringComparison.OrdinalIgnoreCase))
                    return true;
                
                // Special case for options that are followed by values without = (like "-o filename")
                // Only match if it's exactly the dangerous option, not a prefix of another valid option
                if (dangerous.StartsWith("-") && !dangerous.Contains("=") && 
                    opt.StartsWith(dangerous, StringComparison.OrdinalIgnoreCase))
                {
                    // Additional check: make sure we're not matching valid options that start with dangerous prefixes
                    // For example, -pedantic should not match -p
                    return opt.Length == dangerous.Length || !char.IsLetter(opt[dangerous.Length]);
                }
                
                return false;
            }));
                
        if (dangerousOption != null)
            throw new ArgumentException($"Potentially dangerous compiler option detected: {dangerousOption}");
            
        return parsed;
    }

    /// <summary>
    /// Creates compilation result object
    /// </summary>
    private static object CreateCompilationResult(CXTranslationUnit translationUnit)
    {
        var diagnostics = GetDiagnostics(translationUnit);
        // Success: エラーが0件かつ致命的エラーがない場合のみtrue
        bool hasFatal = diagnostics.messages.Any(m => (m as dynamic)?.Severity == "Fatal");
        return new
        {
            Success = diagnostics.errors == 0 && !hasFatal,
            Errors = diagnostics.errors,
            Warnings = diagnostics.warnings,
            Diagnostics = diagnostics.messages,
            SourceFile = ClangConstants.DefaultSourceFileName
        };
    }

    /// <summary>
    /// Creates analysis result object
    /// </summary>
    private static object CreateAnalysisResult(CXTranslationUnit translationUnit)
    {
        var diagnostics = GetDiagnostics(translationUnit);
        return new
        {
            Success = true,
            Issues = diagnostics.errors + diagnostics.warnings,
            Findings = diagnostics.messages,
            SourceFile = ClangConstants.DefaultSourceFileName
        };
    }

    /// <summary>
    /// Creates AST result object asynchronously
    /// </summary>
    private static async Task<object> CreateAstResultAsync(CXTranslationUnit translationUnit)
    {
        var astNodes = new List<AstNode>();
        var cursor = translationUnit.Cursor;
        
        await Task.Run(() => VisitAST(cursor, astNodes, 0));
        
        return new
        {
            Success = true,
            SourceFile = ClangConstants.DefaultSourceFileName,
            Ast = astNodes
        };
    }
    
    /// <summary>
    /// Extracts diagnostics from translation unit with proper resource management
    /// </summary>
    private static (int errors, int warnings, List<object> messages) GetDiagnostics(CXTranslationUnit translationUnit)
    {
        var messages = new List<object>();
        int errors = 0, warnings = 0;
        var diagnosticsCount = translationUnit.NumDiagnostics;
        for (uint i = 0; i < diagnosticsCount; i++)
        {
            using var diagnostic = translationUnit.GetDiagnostic(i);
            var severity = diagnostic.Severity;
            var message = diagnostic.Spelling.CString;
            var location = diagnostic.Location;
            location.GetFileLocation(out var file, out var line, out var column, out _);
            var fileName = file.Name.CString ?? ClangConstants.DefaultSourceFileName;
            var diagnosticInfo = new
            {
                Severity = severity.ToString(),
                Message = message ?? "Unknown error",
                File = fileName,
                Line = line,
                Column = column
            };
            messages.Add(diagnosticInfo);
            // ClangのNoteやRemarkはカウントしない
            if (severity == CXDiagnosticSeverity.CXDiagnostic_Error || severity == CXDiagnosticSeverity.CXDiagnostic_Fatal)
                errors++;
            else if (severity == CXDiagnosticSeverity.CXDiagnostic_Warning)
                warnings++;
        }
    // 診断が0件の場合はエラーをカウントしない（正常終了とみなす）
        // 診断が0件かつtranslationUnit.Handle==IntPtr.Zeroならエラー扱い
        if (errors == 0 && translationUnit.NumDiagnostics == 0 && translationUnit.Handle == IntPtr.Zero)
        {
            errors = 1;
            messages.Add(new { Severity = "Error", Message = "Failed to create translation unit (no diagnostics)", File = ClangConstants.DefaultSourceFileName, Line = 0, Column = 0 });
        }
        return (errors, warnings, messages);
    }
    
    /// <summary>
    /// Visits AST nodes recursively with proper type safety
    /// </summary>
    private static void VisitAST(CXCursor cursor, List<AstNode> nodes, int depth)
    {
        if (depth > ClangConstants.MaxAstDepth) 
        {
            Logger.LogDebug("Maximum AST depth reached, stopping traversal");
            return;
        }
            
        var kind = cursor.Kind;
        var spelling = cursor.Spelling.CString ?? string.Empty;
        cursor.Location.GetFileLocation(out _, out var line, out var column, out _);
        
        var node = new AstNode(kind.ToString(), spelling, line, column);
        nodes.Add(node);
        
        unsafe
        {
            cursor.VisitChildren((child, parent, data) =>
            {
                VisitAST(child, node.Children, depth + 1);
                return CXChildVisitResult.CXChildVisit_Continue;
            }, default(CXClientData));
        }
    }

    /// <summary>
    /// Safely cleans up temporary directory with retry logic
    /// </summary>
    private static async Task CleanupDirectoryAsync(string directory)
    {
        if (!Directory.Exists(directory)) 
            return;
        
        for (int attempts = 0; attempts < ClangConstants.CleanupRetryAttempts; attempts++)
        {
            try
            {
                Directory.Delete(directory, true);
                Logger.LogDebug("Successfully cleaned up temporary directory: {Directory}", directory);
                return;
            }
            catch (IOException ex) when (attempts < ClangConstants.CleanupRetryAttempts - 1)
            {
                Logger.LogDebug(ex, "Failed to cleanup directory (attempt {Attempt}/{Total}), retrying...", 
                    attempts + 1, ClangConstants.CleanupRetryAttempts);
                await Task.Delay(ClangConstants.CleanupDelayMs);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to cleanup temporary directory: {Directory}", directory);
                break;
            }
        }
        
        Logger.LogWarning("Failed to cleanup temporary directory after {Attempts} attempts: {Directory}", 
            ClangConstants.CleanupRetryAttempts, directory);
    }
    
    /// <summary>
    /// Creates a standardized error result
    /// </summary>
    private static string CreateErrorResult(string message)
    {
        var errorResult = new
        {
            Success = false,
            Error = message ?? "Unknown error occurred"
        };
        return JsonSerializer.Serialize(errorResult, JsonOptions);
    }
}