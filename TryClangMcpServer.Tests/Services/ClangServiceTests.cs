using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using TryClangMcpServer.Configuration;
using TryClangMcpServer.Models;
using TryClangMcpServer.Services;
using TryClangMcpServer.Tests.Helpers;

namespace TryClangMcpServer.Tests.Services;

public class ClangServiceTests
{
    private ClangService _clangService = null!;
    private ILogger<ClangService> _logger = null!;
    private ClangOptions _options = null!;

    [SetUp]
    public void Setup()
    {
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ClangService>();
        _options = new ClangOptions();
        _clangService = new ClangService(_logger, Options.Create(_options));
    }

    [TearDown]
    public void TearDown()
    {
        _clangService?.Dispose();
    }

    [Test]
    public async Task CompileCppAsync_WithValidCode_ShouldSucceed()
    {
        // Arrange
        var sourceCode = @"
int main() {
    return 0;
}";

        // Act
        var result = await _clangService.CompileCppAsync(sourceCode);

        // Assert
        Assert.That(result.Success, Is.True, "Compilation should succeed for valid code");
        
        var compilationResult = ClangTestHelper.ParseClangResult(result);
        Assert.That(compilationResult.Success, Is.True);
        Assert.That(compilationResult.Errors, Is.EqualTo(0), "Should have no compilation errors");
    }

    [Test]
    public async Task CompileCppAsync_WithInvalidCode_ShouldReturnErrors()
    {
        // Arrange
        var sourceCode = @"
int main() {
    undeclared_variable = 42;
    return 0;
}";

        // Act
        var result = await _clangService.CompileCppAsync(sourceCode);

        // Assert
        Assert.That(result.Success, Is.True, "Operation should complete successfully even with compile errors");
        
        var compilationResult = ClangTestHelper.ParseClangResult(result);
        
        // Debug output
        Console.WriteLine($"Compilation Success: {compilationResult.Success}");
        Console.WriteLine($"Error Count: {compilationResult.Errors}");
        Console.WriteLine($"Warning Count: {compilationResult.Warnings}");
        Console.WriteLine($"Diagnostics Count: {compilationResult.Diagnostics.Count}");
        
        foreach (var diagnostic in compilationResult.Diagnostics)
        {
            Console.WriteLine($"  {diagnostic.Severity}: {diagnostic.Message} at {diagnostic.Line}:{diagnostic.Column}");
        }
        
        Assert.That(compilationResult.Success, Is.False, "Compilation should fail for invalid code");
        Assert.That(compilationResult.Errors, Is.GreaterThan(0), "Should have compilation errors");
    }

    [Test]
    public async Task AnalyzeCppAsync_WithValidCode_ShouldReturnAnalysis()
    {
        // Arrange
        var sourceCode = @"
int main() {
    int unused_variable = 42;
    return 0;
}";

        // Act
        var result = await _clangService.AnalyzeCppAsync(sourceCode);

        // Assert
        Assert.That(result.Success, Is.True, "Analysis should complete successfully");
        
        var analysisResult = ClangTestHelper.ParseClangResult(result);
        Assert.That(analysisResult.Success, Is.True);
    }

    [Test]
    public async Task GetAstAsync_WithValidCode_ShouldReturnAst()
    {
        // Arrange
        var sourceCode = @"
int add(int a, int b) {
    return a + b;
}";

        // Act
        var result = await _clangService.GetAstAsync(sourceCode);

        // Assert
        Assert.That(result.Success, Is.True, "AST generation should succeed");
        
        var astResult = ClangTestHelper.ParseClangResult(result);
        Assert.That(astResult.Success, Is.True);
        Assert.That(astResult.Ast, Is.Not.Empty, "Result should contain AST data");
    }

    [Test]
    public async Task CompileCppAsync_WithOptions_ShouldUseOptions()
    {
        // Arrange
        var sourceCode = @"
int main() {
    return 0;
}";

        // Act
        var result = await _clangService.CompileCppAsync(sourceCode, "-std=c++17 -Wall");

        // Assert
        Assert.That(result.Success, Is.True, "Compilation with options should succeed");
        
        var compilationResult = ClangTestHelper.ParseClangResult(result);
        Assert.That(compilationResult.Success, Is.True);
    }

    [Test]
    public async Task CompileCppAsync_WithEmptySourceCode_ShouldReturnError()
    {
        // Act
        var result = await _clangService.CompileCppAsync("");

        // Assert
        Assert.That(result.Success, Is.False, "Should fail with empty source code");
        Assert.That(result.Error, Is.Not.Null.And.Not.Empty, "Should contain error message");
    }

    [Test]
    public async Task CompileCppAsync_WithNullSourceCode_ShouldReturnError()
    {
        // Act
        var result = await _clangService.CompileCppAsync(null!);

        // Assert
        Assert.That(result.Success, Is.False, "Should fail with null source code");
        Assert.That(result.Error, Is.Not.Null.And.Not.Empty, "Should contain error message");
    }

    [Test]
    public async Task CompileCppAsync_WithDangerousOptions_ShouldReturnError()
    {
        // Arrange
        var sourceCode = "int main() { return 0; }";

        // Act
        var result = await _clangService.CompileCppAsync(sourceCode, "-o /etc/passwd");

        // Assert
        Assert.That(result.Success, Is.False, "Should reject dangerous options");
        Assert.That(result.Error, Does.Contain("dangerous"), "Should contain error message about dangerous options");
    }

    [Test]
    public async Task AnalyzeCppAsync_WithEmptySourceCode_ShouldReturnError()
    {
        // Act
        var result = await _clangService.AnalyzeCppAsync("");

        // Assert
        Assert.That(result.Success, Is.False, "Analysis should fail with empty source code");
    }

    [Test]
    public async Task GetAstAsync_WithEmptySourceCode_ShouldReturnError()
    {
        // Act
        var result = await _clangService.GetAstAsync("");

        // Assert
        Assert.That(result.Success, Is.False, "AST generation should fail with empty source code");
    }

    [Test]
    public async Task CompileCppAsync_WithLargeSourceCode_ShouldReturnError()
    {
        // Arrange - Create source code larger than 1MB limit
        var largeSourceCode = new string('/', 1_100_000) + "\nint main() { return 0; }";

        // Act
        var result = await _clangService.CompileCppAsync(largeSourceCode);

        // Assert
        Assert.That(result.Success, Is.False, "Should reject source code that's too large");
        Assert.That(result.Error, Does.Contain("too large"), "Should contain error message about size limit");
    }
}