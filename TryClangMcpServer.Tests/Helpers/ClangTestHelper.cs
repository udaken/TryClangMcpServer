using System.Text.Json;
using TryClangMcpServer.Models;

namespace TryClangMcpServer.Tests.Helpers;

public static class ClangTestHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static T ParseClangResult<T>(ClangResult<T> result)
    {
        if (!result.Success)
            throw new InvalidOperationException($"Clang operation failed: {result.Error}");
            
        return result.Data ?? throw new InvalidOperationException("Result data is null");
    }
    
    public static bool? GetBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && 
               (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            ? property.GetBoolean()
            : null;
    }

    public static int? GetInt32Property(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }
    
    public static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}