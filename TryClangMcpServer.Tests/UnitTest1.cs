using System.Text.Json;

namespace TryClangMcpServer.Tests;

public class ClangToolsTests
{
    [Test]
    public async Task CompileCpp_WithValidCode_ShouldSucceed()
    {
        // Arrange
        var sourceCode = @"
int main() {
    return 0;
}";

        // Act
        var result = await Clang.CompileCpp(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        // Handle both camelCase (new) and original property names for backward compatibility
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? false;
        var errors = GetInt32Property(jsonDoc.RootElement, "errors") ?? 
                    GetInt32Property(jsonDoc.RootElement, "Errors") ?? -1;
                    
        Assert.That(success, Is.True, "Compilation should succeed for valid code");
        Assert.That(errors, Is.EqualTo(0), "Should have no compilation errors");
    }

    [Test]
    public async Task CompileCpp_WithInvalidCode_ShouldReturnErrors()
    {
        // Arrange
        var sourceCode = @"
int main() {
    undeclared_variable = 42;
    return 0;
}";

        // Act
        var result = await Clang.CompileCpp(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? true;
        var errors = GetInt32Property(jsonDoc.RootElement, "errors") ?? 
                    GetInt32Property(jsonDoc.RootElement, "Errors") ?? 0;
                    
        Assert.That(success, Is.False, "Compilation should fail for invalid code");
        Assert.That(errors, Is.GreaterThan(0), "Should have compilation errors");
    }

    [Test]
    public async Task AnalyzeCpp_WithValidCode_ShouldReturnAnalysis()
    {
        // Arrange
        var sourceCode = @"
int main() {
    int unused_variable = 42;
    return 0;
}";

        // Act
        var result = await Clang.AnalyzeCpp(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? false;
                     
        Assert.That(success, Is.True, "Analysis should complete successfully");
    }

    [Test]
    public async Task GetAst_WithValidCode_ShouldReturnAst()
    {
        // Arrange
        var sourceCode = @"
int add(int a, int b) {
    return a + b;
}";

        // Act
        var result = await Clang.GetAst(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? false;
        var hasAst = jsonDoc.RootElement.TryGetProperty("ast", out _) ||
                    jsonDoc.RootElement.TryGetProperty("Ast", out _);
                    
        Assert.That(success, Is.True, "AST generation should succeed");
        Assert.That(hasAst, Is.True, "Result should contain AST data");
    }

    [Test]
    public async Task CompileCpp_WithOptions_ShouldUseOptions()
    {
        // Arrange
        var sourceCode = @"
int main() {
    return 0;
}";

        // Act
        var result = await Clang.CompileCpp(sourceCode, "-std=c++17 -Wall");

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? false;
                     
        Assert.That(success, Is.True, "Compilation with options should succeed");
    }

    [Test]
    public async Task CompileCpp_WithEmptySourceCode_ShouldReturnError()
    {
        // Arrange
        var sourceCode = "";

        // Act
        var result = await Clang.CompileCpp(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? true;
        var hasError = jsonDoc.RootElement.TryGetProperty("error", out _) ||
                      jsonDoc.RootElement.TryGetProperty("Error", out _);
                      
        Assert.That(success, Is.False, "Should fail with empty source code");
        Assert.That(hasError, Is.True, "Should contain error message");
    }

    [Test]
    public async Task CompileCpp_WithNullSourceCode_ShouldReturnError()
    {
        // Act
        var result = await Clang.CompileCpp(null!);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? true;
        var hasError = jsonDoc.RootElement.TryGetProperty("error", out _) ||
                      jsonDoc.RootElement.TryGetProperty("Error", out _);
                      
        Assert.That(success, Is.False, "Should fail with null source code");
        Assert.That(hasError, Is.True, "Should contain error message");
    }

    [Test]
    public async Task CompileCpp_WithDangerousOptions_ShouldReturnError()
    {
        // Arrange
        var sourceCode = "int main() { return 0; }";

        // Act
        var result = await Clang.CompileCpp(sourceCode, "-o /etc/passwd");

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? true;
        var hasError = jsonDoc.RootElement.TryGetProperty("error", out _) ||
                      jsonDoc.RootElement.TryGetProperty("Error", out _);
                      
        Assert.That(success, Is.False, "Should reject dangerous options");
        Assert.That(hasError, Is.True, "Should contain error message about dangerous options");
    }

    [Test]
    public async Task AnalyzeCpp_WithEmptySourceCode_ShouldReturnError()
    {
        // Act
        var result = await Clang.AnalyzeCpp("");

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? true;
                     
        Assert.That(success, Is.False, "Analysis should fail with empty source code");
    }

    [Test]
    public async Task GetAst_WithEmptySourceCode_ShouldReturnError()
    {
        // Act
        var result = await Clang.GetAst("");

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? true;
                     
        Assert.That(success, Is.False, "AST generation should fail with empty source code");
    }

    [Test]
    public async Task CompileCpp_WithLargeSourceCode_ShouldReturnError()
    {
        // Arrange - Create source code larger than 1MB limit
        var largeSourceCode = new string('/', 1_100_000) + "\nint main() { return 0; }";

        // Act
        var result = await Clang.CompileCpp(largeSourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        
        var success = GetBooleanProperty(jsonDoc.RootElement, "success") ?? 
                     GetBooleanProperty(jsonDoc.RootElement, "Success") ?? true;
        var hasError = jsonDoc.RootElement.TryGetProperty("error", out _) ||
                      jsonDoc.RootElement.TryGetProperty("Error", out _);
                      
        Assert.That(success, Is.False, "Should reject source code that's too large");
        Assert.That(hasError, Is.True, "Should contain error message about size limit");
    }

    // Helper methods to handle both camelCase and PascalCase properties
    private static bool? GetBooleanProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static int? GetInt32Property(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : null;
    }
}
