using System.ComponentModel;
using System.Text;
using ClangSharp;
using ClangSharp.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using TryClangMcpServer.Configuration;
using TryClangMcpServer.Models;

namespace TryClangMcpServer.Services;

[McpServerToolType]
public class ClangService : IClangService
{
    private readonly ILogger<ClangService> _logger;
    private readonly ClangOptions _options;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public ClangService(ILogger<ClangService> logger, IOptions<ClangOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentOperations, _options.MaxConcurrentOperations);
    }

    [McpServerTool, Description("Compiles C/C++ code with various options and returns diagnostics")]
    public async Task<ClangResult<CompilationResult>> CompileCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic")
    {
        return await ExecuteClangOperationAsync<CompilationResult>(sourceCode, options, ClangOperation.Compile, "compilation");
    }

    [McpServerTool, Description("Performs static analysis using Clang Static Analyzer")]
    public async Task<ClangResult<AnalysisResult>> AnalyzeCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic")
    {
        return await ExecuteClangOperationAsync<AnalysisResult>(sourceCode, options, ClangOperation.Analyze, "analysis");
    }

    [McpServerTool, Description("Generates Abstract Syntax Trees in JSON format")]
    public async Task<ClangResult<AstResult>> GetAstAsync(string sourceCode, string options = "")
    {
        return await ExecuteClangOperationAsync<AstResult>(sourceCode, options, ClangOperation.GenerateAST, "AST generation");
    }

    /// <summary>
    /// Executes a Clang operation with proper resource management and error handling
    /// </summary>
    private async Task<ClangResult<T>> ExecuteClangOperationAsync<T>(
        string sourceCode, 
        string options, 
        ClangOperation operation,
        string operationName)
    {
        // Wait for available slot (with timeout)
        using var timeoutCts = new CancellationTokenSource(_options.OperationTimeoutMs);
        
        if (!await _concurrencyLimiter.WaitAsync(5000, timeoutCts.Token)) // 5 second wait for slot
        {
            _logger.LogWarning("Operation {Operation} rejected due to concurrency limit", operationName);
            return ClangResult<T>.Fail($"{operationName} temporarily unavailable due to high load");
        }
        
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        try
        {
            // Validate inputs
            var validationResult = ValidateSourceCode(sourceCode);
            if (!validationResult.Success)
                return ClangResult<T>.Fail(validationResult.Error!);

            var args = ValidateAndParseOptions(options);
            if (!args.Success)
                return ClangResult<T>.Fail(args.Error!);

            // Setup temporary directory and file
            Directory.CreateDirectory(tempDir);
            var sourceFile = Path.Combine(tempDir, _options.DefaultSourceFileName);
            await File.WriteAllTextAsync(sourceFile, sourceCode, Encoding.UTF8);
            
            // Use proper resource management
            await using var context = new ClangOperationContext(tempDir, _logger, Options.Create(_options));
            
            var compilationArgs = new List<string>(args.Data!) { "-c" }.ToArray();

            // Prepare arguments based on operation
            var finalArgs = operation == ClangOperation.Analyze 
                ? new List<string> { sourceFile }.Concat(compilationArgs).Concat(new[] { "--analyze" }).ToArray()
                : new List<string> { sourceFile }.Concat(compilationArgs).ToArray();
            
            _logger.LogDebug("Creating translation unit for {SourceFile} with args: [{Args}]", 
                sourceFile, string.Join(", ", finalArgs));

            context.TranslationUnit = CXTranslationUnit.Parse(
                context.Index, sourceFile, finalArgs, [], 
                CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord);
            
            if (context.TranslationUnit?.Handle == IntPtr.Zero)
            {
                _logger.LogError("Failed to create translation unit for {Operation}. SourceFile: {SourceFile}, Args: [{Args}]", 
                    operationName, sourceFile, string.Join(", ", finalArgs));
                
                // Try with minimal arguments for basic C++
                var basicArgs = new[] { "-std=c++17", "-x", "c++" };
                _logger.LogDebug("Attempting fallback with basic args: [{BasicArgs}]", string.Join(", ", basicArgs));
                
                context.TranslationUnit = CXTranslationUnit.Parse(
                    context.Index, sourceFile, basicArgs, [], CXTranslationUnit_Flags.CXTranslationUnit_None);
                    
                if (context.TranslationUnit?.Handle == IntPtr.Zero)
                {
                    return ClangResult<T>.Fail($"Failed to create translation unit for {operationName} (both normal and fallback attempts failed)");
                }
                
                _logger.LogWarning("{Operation} completed using fallback method", operationName);
            }
            
            // Generate result based on operation type
            if (context.TranslationUnit == null)
            {
                return ClangResult<T>.Fail("Failed to create translation unit");
            }
            
            var result = operation switch
            {
                ClangOperation.Compile => (T)(object)CreateCompilationResult(context.TranslationUnit.Value),
                ClangOperation.Analyze => (T)(object)CreateAnalysisResult(context.TranslationUnit.Value),
                ClangOperation.GenerateAST => (T)(object)await CreateAstResultAsync(context.TranslationUnit.Value),
                _ => throw new ArgumentException($"Unknown operation: {operation}", nameof(operation))
            };
            
            _logger.LogDebug("{Operation} completed successfully", operationName);
            return ClangResult<T>.Ok(result);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            _logger.LogWarning("Operation {Operation} timed out after {Timeout}ms", operationName, _options.OperationTimeoutMs);
            return ClangResult<T>.Fail($"{operationName} operation timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during {Operation}: {Message}", operationName, ex.Message);
            return ClangResult<T>.Fail($"{operationName} operation failed due to an internal error");
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Validates source code input
    /// </summary>
    private ClangResult<string> ValidateSourceCode(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            return ClangResult<string>.Fail("Source code cannot be null or empty");
        
        if (Encoding.UTF8.GetByteCount(sourceCode) > _options.MaxSourceCodeSizeBytes)
            return ClangResult<string>.Fail($"Source code too large (max {_options.MaxSourceCodeSizeBytes / 1024}KB)");

        return ClangResult<string>.Ok(sourceCode);
    }

    /// <summary>
    /// Validates and parses compiler options, filtering out dangerous ones
    /// </summary>
    private ClangResult<string[]> ValidateAndParseOptions(string options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return ClangResult<string[]>.Ok(Array.Empty<string>());
            
        var parsed = options.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Check for dangerous options with more precise matching
        var dangerousOption = parsed.FirstOrDefault(opt => 
            _options.DangerousOptions.Any(dangerous => 
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
            return ClangResult<string[]>.Fail($"Potentially dangerous compiler option detected: {dangerousOption}");
            
        return ClangResult<string[]>.Ok(parsed);
    }

    /// <summary>
    /// Creates compilation result object
    /// </summary>
    private CompilationResult CreateCompilationResult(CXTranslationUnit translationUnit)
    {
        var diagnostics = GetDiagnostics(translationUnit);
        // Success: true only when there are 0 errors and no fatal errors
        bool hasFatal = diagnostics.Any(d => d.Severity == "Fatal");
        var errorCount = diagnostics.Count(d => d.Severity is "Error" or "Fatal");
        
        return new CompilationResult(
            Success: errorCount == 0 && !hasFatal,
            Errors: errorCount,
            Warnings: diagnostics.Count(d => d.Severity == "Warning"),
            Diagnostics: diagnostics,
            SourceFile: _options.DefaultSourceFileName
        );
    }

    /// <summary>
    /// Creates analysis result object
    /// </summary>
    private AnalysisResult CreateAnalysisResult(CXTranslationUnit translationUnit)
    {
        var diagnostics = GetDiagnostics(translationUnit);
        var errorCount = diagnostics.Count(d => d.Severity is "Error" or "Fatal");
        var warningCount = diagnostics.Count(d => d.Severity == "Warning");
        
        return new AnalysisResult(
            Success: true,
            Issues: errorCount + warningCount,
            Findings: diagnostics,
            SourceFile: _options.DefaultSourceFileName
        );
    }

    /// <summary>
    /// Creates AST result object asynchronously
    /// </summary>
    private async Task<AstResult> CreateAstResultAsync(CXTranslationUnit translationUnit)
    {
        var astNodes = new List<AstNode>();
        var cursor = translationUnit.Cursor;
        
        await Task.Run(() => VisitAST(cursor, astNodes, 0));
        
        return new AstResult(
            Success: true,
            SourceFile: _options.DefaultSourceFileName,
            Ast: astNodes
        );
    }
    
    /// <summary>
    /// Extracts diagnostics from translation unit with proper resource management
    /// </summary>
    private List<ClangDiagnostic> GetDiagnostics(CXTranslationUnit translationUnit)
    {
        var diagnostics = new List<ClangDiagnostic>();
        var diagnosticsCount = translationUnit.NumDiagnostics;
        
        for (uint i = 0; i < diagnosticsCount; i++)
        {
            using var diagnostic = translationUnit.GetDiagnostic(i);
            var severity = diagnostic.Severity;
            var message = diagnostic.Spelling.CString;
            var location = diagnostic.Location;
            location.GetFileLocation(out var file, out var line, out var column, out _);
            var fileName = file.Name.CString ?? _options.DefaultSourceFileName;
            
            diagnostics.Add(new ClangDiagnostic(
                Severity: severity.ToString(),
                Message: message ?? "Unknown error",
                File: fileName,
                Line: line,
                Column: column
            ));
        }

        // Handle case where translation unit failed to create but no diagnostics were generated
        if (diagnostics.Count == 0 && translationUnit.Handle == IntPtr.Zero)
        {
            diagnostics.Add(new ClangDiagnostic(
                Severity: "Error",
                Message: "Failed to create translation unit (no diagnostics)",
                File: _options.DefaultSourceFileName,
                Line: 0,
                Column: 0
            ));
        }
        
        return diagnostics;
    }
    
    /// <summary>
    /// Visits AST nodes recursively with proper type safety
    /// </summary>
    private void VisitAST(CXCursor cursor, List<AstNode> nodes, int depth)
    {
        if (depth > _options.MaxAstDepth) 
        {
            _logger.LogDebug("Maximum AST depth reached, stopping traversal");
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

    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
    }
}