using Microsoft.Extensions.Logging;
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
    ValueTask<ClangResult<CompilationResult>> CompileCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic");

    /// <summary>
    /// Performs static analysis using Clang Static Analyzer
    /// </summary>
    ValueTask<ClangResult<AnalysisResult>> AnalyzeCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic");

    /// <summary>
    /// Generates Abstract Syntax Trees in JSON format
    /// </summary>
    ValueTask<ClangResult<AstResult>> GetAstAsync(string sourceCode, string options = "");

    /// <summary>
    /// Preprocesses C/C++ code and returns the expanded source
    /// </summary>
    ValueTask<ClangResult<PreprocessResult>> PreprocessCppAsync(string sourceCode, string options = "-std=c++20", IReadOnlyDictionary<string, string>? definitions = null);
}
