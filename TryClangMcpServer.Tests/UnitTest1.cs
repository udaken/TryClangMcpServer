using System.Text.Json;

namespace TryClangMcpServer.Tests;

public class ClangToolsTests
{
    [Test]
    public void CompileCpp_WithValidCode_ShouldSucceed()
    {
        // Arrange
        var sourceCode = @"
#include <iostream>
int main() {
    std::cout << ""Hello, World!"" << std::endl;
    return 0;
}";

        // Act
        var result = Clang.CompileCpp(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        Assert.That(jsonDoc.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(jsonDoc.RootElement.GetProperty("errors").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public void CompileCpp_WithInvalidCode_ShouldReturnErrors()
    {
        // Arrange
        var sourceCode = @"
int main() {
    undeclared_variable = 42;
    return 0;
}";

        // Act
        var result = Clang.CompileCpp(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        Assert.That(jsonDoc.RootElement.GetProperty("success").GetBoolean(), Is.False);
        Assert.That(jsonDoc.RootElement.GetProperty("errors").GetInt32(), Is.GreaterThan(0));
    }

    [Test]
    public void AnalyzeCpp_WithValidCode_ShouldReturnAnalysis()
    {
        // Arrange
        var sourceCode = @"
#include <iostream>
int main() {
    int unused_variable = 42;
    std::cout << ""Hello, World!"" << std::endl;
    return 0;
}";

        // Act
        var result = Clang.AnalyzeCpp(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        Assert.That(jsonDoc.RootElement.GetProperty("success").GetBoolean(), Is.True);
    }

    [Test]
    public void GetAst_WithValidCode_ShouldReturnAst()
    {
        // Arrange
        var sourceCode = @"
int add(int a, int b) {
    return a + b;
}";

        // Act
        var result = Clang.GetAst(sourceCode);

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        Assert.That(jsonDoc.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(jsonDoc.RootElement.TryGetProperty("ast", out var astProperty), Is.True);
    }

    [Test]
    public void CompileCpp_WithOptions_ShouldUseOptions()
    {
        // Arrange
        var sourceCode = @"
#include <iostream>
int main() {
    std::cout << ""Hello, World!"" << std::endl;
    return 0;
}";

        // Act
        var result = Clang.CompileCpp(sourceCode, "-std=c++17 -Wall");

        // Assert
        Assert.That(result, Is.Not.Null);
        var jsonDoc = JsonDocument.Parse(result);
        Assert.That(jsonDoc.RootElement.GetProperty("success").GetBoolean(), Is.True);
    }
}
