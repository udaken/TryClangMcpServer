using System.Text.Json;

namespace TryClangMcpServer.Models;

/// <summary>
/// Represents the result of a Clang operation with consistent error handling
/// </summary>
public record ClangResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }
    public List<ClangDiagnostic> Diagnostics { get; init; } = new();

    public static ClangResult<T> Ok(T data, List<ClangDiagnostic>? diagnostics = null) =>
        new() { Success = true, Data = data, Diagnostics = diagnostics ?? new() };

    public static ClangResult<T> Fail(string error, List<ClangDiagnostic>? diagnostics = null) =>
        new() { Success = false, Error = error, Diagnostics = diagnostics ?? new() };
}

/// <summary>
/// Represents a diagnostic message from Clang
/// </summary>
public record ClangDiagnostic(
    string Severity,
    string Message,
    string File,
    uint Line,
    uint Column);

/// <summary>
/// Represents the type of Clang operation to perform
/// </summary>
public enum ClangOperation
{
    Compile,
    Analyze,
    GenerateAST,
    Preprocess
}

/// <summary>
/// Represents an AST node with strongly-typed properties
/// </summary>
public record AstNode(string Kind, string Spelling, uint Line, uint Column)
{
    public List<AstNode> Children { get; init; } = new();
}