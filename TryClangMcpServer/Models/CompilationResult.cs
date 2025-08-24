namespace TryClangMcpServer.Models;

/// <summary>
/// Represents the result of a C++ compilation operation
/// </summary>
public record CompilationResult(
    bool Success,
    int Errors,
    int Warnings,
    List<ClangDiagnostic> Diagnostics,
    string SourceFile);

/// <summary>
/// Represents the result of a C++ static analysis operation
/// </summary>
public record AnalysisResult(
    bool Success,
    int Issues,
    List<ClangDiagnostic> Findings,
    string SourceFile);

/// <summary>
/// Represents the result of AST generation
/// </summary>
public record AstResult(
    bool Success,
    string SourceFile,
    List<AstNode> Ast);