using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using ClangSharp;
using ClangSharp.Interop;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // Configure all logs to go to stderr
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();

[McpServerToolType]
public static class Clang
{
    private static readonly ILogger Logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<object>();

    [McpServerTool, Description("Compiles C/C++ code with various options and returns diagnostics")]
    public static string CompileCpp(string sourceCode, string options = "")
    {
        try
        {
            // Create temporary file for source code
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            var sourceFile = Path.Combine(tempDir, "source.cpp");
            File.WriteAllText(sourceFile, sourceCode, Encoding.UTF8);
            
            // Parse compilation options
            var args = ParseOptions(options);
            args = [sourceFile, .. args];
            
            // Create compilation unit
            var index = CXIndex.Create();
            var translationUnit = CXTranslationUnit.Parse(index, sourceFile, args, [], CXTranslationUnit_Flags.CXTranslationUnit_None);
            
            if (translationUnit.Handle == IntPtr.Zero)
            {
                return CreateErrorResult("Failed to create translation unit");
            }
            
            // Get diagnostics
            var diagnostics = GetDiagnostics(translationUnit);
            var compilationResult = new
            {
                success = diagnostics.errors == 0,
                errors = diagnostics.errors,
                warnings = diagnostics.warnings,
                diagnostics = diagnostics.messages,
                sourceFile = "source.cpp"
            };
            
            // Cleanup
            translationUnit.Dispose();
            index.Dispose();
            Directory.Delete(tempDir, true);
            
            return System.Text.Json.JsonSerializer.Serialize(compilationResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error compiling C++ code");
            return CreateErrorResult($"Compilation error: {ex.Message}");
        }
    }

    [McpServerTool, Description("Performs static analysis using Clang Static Analyzer.")]
    public static string AnalyzeCpp(string sourceCode, string options = "")
    {
        try
        {
            // Create temporary file for source code
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            var sourceFile = Path.Combine(tempDir, "source.cpp");
            File.WriteAllText(sourceFile, sourceCode, Encoding.UTF8);
            
            // Parse analysis options
            var args = ParseOptions(options);
            args = [sourceFile, .. args, "--analyze"];
            
            // Create compilation unit with static analysis
            var index = CXIndex.Create();
            var translationUnit = CXTranslationUnit.Parse(index, sourceFile, args, [], CXTranslationUnit_Flags.CXTranslationUnit_None);
            
            if (translationUnit.Handle == IntPtr.Zero)
            {
                return CreateErrorResult("Failed to create translation unit for analysis");
            }
            
            // Get analysis results (diagnostics)
            var diagnostics = GetDiagnostics(translationUnit);
            var analysisResult = new
            {
                success = true,
                issues = diagnostics.errors + diagnostics.warnings,
                findings = diagnostics.messages,
                sourceFile = "source.cpp"
            };
            
            // Cleanup
            translationUnit.Dispose();
            index.Dispose();
            Directory.Delete(tempDir, true);
            
            return System.Text.Json.JsonSerializer.Serialize(analysisResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error analyzing C++ code");
            return CreateErrorResult($"Analysis error: {ex.Message}");
        }
    }
    
    [McpServerTool, Description("Generates Abstract Syntax Trees in multiple formats.")]
    public static string GetAst(string sourceCode, string options = "")
    {
        try
        {
            // Create temporary file for source code
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            var sourceFile = Path.Combine(tempDir, "source.cpp");
            File.WriteAllText(sourceFile, sourceCode, Encoding.UTF8);
            
            // Parse AST options
            var args = ParseOptions(options);
            args = [sourceFile, .. args];
            
            // Create compilation unit
            var index = CXIndex.Create();
            var translationUnit = CXTranslationUnit.Parse(index, sourceFile, args, [], CXTranslationUnit_Flags.CXTranslationUnit_None);
            
            if (translationUnit.Handle == IntPtr.Zero)
            {
                return CreateErrorResult("Failed to create translation unit for AST generation");
            }
            
            // Generate AST
            var astNodes = new List<object>();
            var cursor = translationUnit.Cursor;
            
            VisitAST(cursor, astNodes, 0);
            
            var astResult = new
            {
                success = true,
                sourceFile = "source.cpp",
                ast = astNodes
            };
            
            // Cleanup
            translationUnit.Dispose();
            index.Dispose();
            Directory.Delete(tempDir, true);
            
            return System.Text.Json.JsonSerializer.Serialize(astResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error generating AST");
            return CreateErrorResult($"AST generation error: {ex.Message}");
        }
    }
    
    private static string[] ParseOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return [];
            
        return options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
    
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
            var fileName = file.Name.CString;
            
            var diagnosticInfo = new
            {
                severity = severity.ToString(),
                message,
                file = fileName,
                line,
                column
            };
            
            messages.Add(diagnosticInfo);
            
            if (severity == CXDiagnosticSeverity.CXDiagnostic_Error || severity == CXDiagnosticSeverity.CXDiagnostic_Fatal)
                errors++;
            else if (severity == CXDiagnosticSeverity.CXDiagnostic_Warning)
                warnings++;
        }
        
        return (errors, warnings, messages);
    }
    
    private static void VisitAST(CXCursor cursor, List<object> nodes, int depth)
    {
        if (depth > 10) // Prevent infinite recursion
            return;
            
        var kind = cursor.Kind;
        var spelling = cursor.Spelling.CString;
        cursor.Location.GetFileLocation(out _, out var line, out var column, out _);
        
        var node = new
        {
            kind = kind.ToString(),
            spelling,
            line,
            column,
            children = new List<object>()
        };
        
        nodes.Add(node);
        
        unsafe
        {
            cursor.VisitChildren((child, parent, data) =>
            {
                var childNodes = (List<object>)((dynamic)node).children;
                VisitAST(child, childNodes, depth + 1);
                return CXChildVisitResult.CXChildVisit_Continue;
            }, default(CXClientData));
        }
    }
    
    private static string CreateErrorResult(string message)
    {
        var errorResult = new
        {
            success = false,
            error = message
        };
        return System.Text.Json.JsonSerializer.Serialize(errorResult, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}