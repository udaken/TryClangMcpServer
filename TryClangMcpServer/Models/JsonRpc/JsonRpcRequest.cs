using System.Text.Json;

namespace TryClangMcpServer.Models.JsonRpc;

public record JsonRpcRequest(
    string JsonRpc,
    string Method,
    JsonElement? Params,
    int Id
);

public record JsonRpcResponse(
    string JsonRpc,
    int Id,
    object? Result = null,
    JsonRpcError? Error = null
);

public record JsonRpcError(
    int Code,
    string Message,
    object? Data = null
);

public record ToolCallParams(
    string Name,
    JsonElement Arguments
);

public record ValidationResult(bool IsValid, string? Error = null)
{
    public static ValidationResult Success() => new(true);
    public static ValidationResult Failure(string error) => new(false, error);
}

public record ToolDefinition(
    string Name,
    string Description,
    object InputSchema
);