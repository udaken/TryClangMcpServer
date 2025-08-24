using System.Collections.Generic;

namespace TryClangMcpServer.Models;

/// <summary>
/// Result of C++ code preprocessing operation
/// </summary>
public record PreprocessResult(
    bool Success,
    string PreprocessedCode,
    List<ClangDiagnostic> Diagnostics,
    List<string> IncludedFiles,
    int Errors,
    int Warnings
);