using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;
using TryClangMcpServer.Extensions;

// Check if HTTP mode is requested via command line argument
var useHttpMode = args.Contains("--http");

if (useHttpMode)
{
    // HTTP mode configuration
    var builder = WebApplication.CreateBuilder(args);
    
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Information;
    });
    
    // Add CORS
    builder.Services.AddCors();
    
    // Add controllers for HTTP endpoints
    builder.Services.AddControllers();
    
    // Add MCP server services
    builder.Services
        .AddMcpServer()
        .WithToolsFromAssembly();
        
    // Add custom Clang services
    builder.Services.AddClangServices(builder.Configuration);
    
    var app = builder.Build();
    
    // Configure CORS with environment-aware security
    app.UseCors(policy =>
    {
        var isDevelopment = app.Environment.IsDevelopment();
        
        if (isDevelopment)
        {
            // Development: Allow localhost only
            policy.WithOrigins("http://localhost:*", "https://localhost:*", 
                              "http://127.0.0.1:*", "https://127.0.0.1:*")
                  .SetIsOriginAllowedToAllowWildcardSubdomains()
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // Production: Strict CORS policy
            policy.WithOrigins() // No origins allowed by default
                  .WithMethods("POST") // Only POST for MCP
                  .WithHeaders("Content-Type", "Accept")
                  .DisallowCredentials();
        }
    });
    
    // Use controller routing
    app.MapControllers();
    
    // Add health check endpoint
    app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });
    
    var port = GetPortFromArgs(args) ?? 3000;
    app.Urls.Add($"http://localhost:{port}");
    
    // Only log to stderr in HTTP mode to avoid interfering with MCP protocol
    Console.Error.WriteLine($"🚀 MCP Server running in HTTP mode on http://localhost:{port}");
    Console.Error.WriteLine("📋 Available endpoints:");
    Console.Error.WriteLine($"  • Health: GET http://localhost:{port}/health");
    Console.Error.WriteLine($"  • Tools: POST http://localhost:{port}/mcp");
    Console.Error.WriteLine("📝 Example requests:");
    Console.Error.WriteLine("  • List tools: POST /mcp with {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
    Console.Error.WriteLine("  • Call tool: POST /mcp with {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"compile_cpp\",\"arguments\":{\"sourceCode\":\"int main(){return 0;}\"}}}");
    
    await app.RunAsync();
}
else
{
    // Stdio mode configuration (default for Claude Desktop)
    var builder = Host.CreateApplicationBuilder(args);
    
    // Completely disable logging in stdio mode to avoid interfering with MCP JSON protocol
    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.None);
    
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
        
    // Add custom Clang services
    builder.Services.AddClangServices(builder.Configuration);
        
    await builder.Build().RunAsync();
}

static int? GetPortFromArgs(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--port" && int.TryParse(args[i + 1], out var port))
        {
            return port;
        }
    }
    return null;
}