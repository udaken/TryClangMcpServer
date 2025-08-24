using TryClangMcpServer.Models;

namespace TryClangMcpServer.Services;

/// <summary>
/// Service interface for Clang operations
/// </summary>
public interface IClangService
{
    /// <summary>
    /// Compiles C/C++ code with various options and returns diagnostics
    /// </summary>
    Task<ClangResult<CompilationResult>> CompileCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic");

    /// <summary>
    /// Performs static analysis using Clang Static Analyzer
    /// </summary>
    Task<ClangResult<AnalysisResult>> AnalyzeCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic");

    /// <summary>
    /// Generates Abstract Syntax Trees in JSON format
    /// </summary>
    Task<ClangResult<AstResult>> GetAstAsync(string sourceCode, string options = "");
}