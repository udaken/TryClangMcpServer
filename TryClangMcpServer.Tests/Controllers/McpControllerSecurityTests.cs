using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using TryClangMcpServer.Configuration;
using TryClangMcpServer.Controllers;
using TryClangMcpServer.Services;

namespace TryClangMcpServer.Tests.Controllers;

[TestFixture]
public class McpControllerSecurityTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    services.Configure<ClangOptions>(options =>
                    {
                        options.RateLimitRequestsPerMinute = 2;
                        options.MaxRequestsPerHour = 20; // Must be >= 10 per validation range
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
    }

    [Test]
    public async Task HandleMcpRequest_ExceedsRateLimit_ReturnsRateLimitError()
    {
        var validRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list"
        };
        var json = JsonSerializer.Serialize(validRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response1 = await _client.PostAsync("/mcp", content);
        var response2 = await _client.PostAsync("/mcp", new StringContent(json, Encoding.UTF8, "application/json"));
        var response3 = await _client.PostAsync("/mcp", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.That(response1.StatusCode, Is.EqualTo(HttpStatusCode.OK), "First request should succeed");
        Assert.That(response2.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Second request should succeed");
        Assert.That(response3.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "Third request should be rate limited");

        var errorContent = await response3.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonElement>(errorContent);

        Assert.That(errorResponse.TryGetProperty("error", out var error), Is.True);
        Assert.That(error.TryGetProperty("code", out var code), Is.True);
        Assert.That(code.GetInt32(), Is.EqualTo(-32099), "Should return rate limit error code");
    }

    [Test]
    public async Task HandleMcpRequest_InvalidJsonRpcVersion_ReturnsBadRequest()
    {
        var invalidRequest = new
        {
            jsonrpc = "1.0",
            id = 1,
            method = "tools/list"
        };
        var json = JsonSerializer.Serialize(invalidRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var errorContent = await response.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonElement>(errorContent);

        Assert.That(errorResponse.TryGetProperty("error", out var error), Is.True);
        Assert.That(error.TryGetProperty("code", out var code), Is.True);
        Assert.That(code.GetInt32(), Is.EqualTo(-32600), "Should return invalid request error code");
    }

    [Test]
    public async Task HandleMcpRequest_MissingMethod_ReturnsBadRequest()
    {
        var invalidRequest = new
        {
            jsonrpc = "2.0",
            id = 1
        };
        var json = JsonSerializer.Serialize(invalidRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var errorContent = await response.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonElement>(errorContent);

        Assert.That(errorResponse.TryGetProperty("error", out var error), Is.True);
        Assert.That(error.TryGetProperty("message", out var message), Is.True);
        Assert.That(message.GetString(), Does.Contain("method"));
    }

    [Test]
    public async Task HandleMcpRequest_InvalidJson_ReturnsParseError()
    {
        var invalidJson = "{ invalid json }";
        var content = new StringContent(invalidJson, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var errorContent = await response.Content.ReadAsStringAsync();

        // ASP.NET Core model binding handles JSON parsing errors before reaching the controller
        // so the response format is different - it doesn't have the JSON-RPC error structure
        Assert.That(errorContent, Is.Not.Empty);
        Assert.That(errorContent, Does.Contain("error").Or.Contain("invalid").Or.Contain("problem"));
    }

    [Test]
    public async Task HandleMcpRequest_UnsupportedMethod_ReturnsMethodNotFound()
    {
        var invalidRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "unsupported/method"
        };
        var json = JsonSerializer.Serialize(invalidRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var errorContent = await response.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonElement>(errorContent);

        Assert.That(errorResponse.TryGetProperty("error", out var error), Is.True);
        Assert.That(error.TryGetProperty("code", out var code), Is.True);
        Assert.That(code.GetInt32(), Is.EqualTo(-32600), "Should return invalid request error code for unsupported method");
    }

    [Test]
    public async Task HandleMcpRequest_ToolCallWithEmptySourceCode_ReturnsInvalidParams()
    {
        var invalidRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "compile_cpp",
                arguments = new
                {
                    sourceCode = ""
                }
            }
        };
        var json = JsonSerializer.Serialize(invalidRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Request should be processed");

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseContent);

        Assert.That(responseObject.TryGetProperty("error", out var error), Is.True);
        Assert.That(error.TryGetProperty("code", out var code), Is.True);
        Assert.That(code.GetInt32(), Is.EqualTo(-32602), "Should return invalid params error code");
    }

    [Test]
    public async Task HandleMcpRequest_ToolCallWithUnknownTool_ReturnsInvalidParams()
    {
        var invalidRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "unknown_tool",
                arguments = new
                {
                    sourceCode = "int main() { return 0; }"
                }
            }
        };
        var json = JsonSerializer.Serialize(invalidRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Request should be processed");

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseContent);

        Assert.That(responseObject.TryGetProperty("error", out var error), Is.True);
        Assert.That(error.TryGetProperty("code", out var code), Is.True);
        Assert.That(code.GetInt32(), Is.EqualTo(-32602), "Should return invalid params error code");
    }

    [Test]
    public async Task HandleMcpRequest_ValidToolsList_ReturnsSuccess()
    {
        var validRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list"
        };
        var json = JsonSerializer.Serialize(validRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var responseContent = await response.Content.ReadAsStringAsync();
        var responseObject = JsonSerializer.Deserialize<JsonElement>(responseContent);

        Assert.That(responseObject.TryGetProperty("result", out var result), Is.True);
        Assert.That(result.TryGetProperty("tools", out var tools), Is.True);
        Assert.That(tools.GetArrayLength(), Is.EqualTo(3), "Should return 3 tools");
    }

    [Test]
    public async Task SecurityMiddleware_NonPostRequest_ReturnsMethodNotAllowed()
    {
        var response = await _client.GetAsync("/mcp");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
    }

    [Test]
    public async Task SecurityMiddleware_InvalidContentType_ReturnsUnsupportedMediaType()
    {
        var content = new StringContent("test", Encoding.UTF8, "text/plain");

        var response = await _client.PostAsync("/mcp", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
    }
}