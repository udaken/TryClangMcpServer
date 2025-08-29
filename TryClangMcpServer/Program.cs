using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.AspNetCore;
using TryClangMcpServer.Extensions;
using TryClangMcpServer.Middleware;

namespace TryClangMcpServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var useHttpMode = args.Contains("--http") || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing";

        if (useHttpMode)
        {
            var app = CreateWebApplication(args);
            await app.RunAsync();
        }
        else
        {
            var host = CreateHostApplication(args);
            await host.RunAsync();
        }
    }

    public static WebApplication CreateWebApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureLogging(builder);
        ConfigureServices(builder);

        var app = builder.Build();

        ConfigureMiddleware(app);
        ConfigureEndpoints(app);
        ConfigureConsoleOutput(app, args);

        return app;
    }

    public static IHost CreateHostApplication(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Disable logging for stdio mode to avoid interfering with MCP JSON protocol
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        builder.Services.AddClangServices(builder.Configuration);

        return builder.Build();
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing")
        {
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Information;
            });
        }
    }

    private static void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddCors();
        builder.Services.AddControllers();

        builder.Services
            .AddMcpServer()
            .WithToolsFromAssembly();

        builder.Services.AddClangServices(builder.Configuration);
        builder.Services.AddModernLogging();
        builder.Services.AddSecurityFeatures(builder.Configuration);
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        app.UseMiddleware<SecurityMiddleware>();

        app.UseCors(policy =>
        {
            var isDevelopment = app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing";

            if (isDevelopment)
            {
                policy.WithOrigins("http://localhost:*", "https://localhost:*",
                                  "http://127.0.0.1:*", "https://127.0.0.1:*")
                      .SetIsOriginAllowedToAllowWildcardSubdomains()
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            }
            else
            {
                policy.WithOrigins()
                      .WithMethods("POST")
                      .WithHeaders("Content-Type", "Accept")
                      .DisallowCredentials();
            }
        });

        app.UseRouting();
    }

    private static void ConfigureEndpoints(WebApplication app)
    {
        app.MapControllers();

        // Health check endpoint
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                var result = new
                {
                    status = report.Status.ToString(),
                    timestamp = DateTime.UtcNow,
                    checks = report.Entries.Select(entry => new
                    {
                        name = entry.Key,
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                        duration = entry.Value.Duration.TotalMilliseconds,
                        data = entry.Value.Data
                    })
                };

                await context.Response.WriteAsJsonAsync(result);
            }
        });

        // Simple health endpoint for quick checks
        app.MapGet("/health/ready", () => new { status = "ready", timestamp = DateTime.UtcNow });
    }

    private static void ConfigureConsoleOutput(WebApplication app, string[] args)
    {
        var port = GetPortFromArgs(args) ?? 3000;

        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Testing")
        {
            app.Urls.Add($"http://localhost:{port}");

            Console.Error.WriteLine($"🚀 MCP Server running in HTTP mode on http://localhost:{port}");
            Console.Error.WriteLine("📋 Available endpoints:");
            Console.Error.WriteLine($"  • Health: GET http://localhost:{port}/health");
            Console.Error.WriteLine($"  • Ready Check: GET http://localhost:{port}/health/ready");
            Console.Error.WriteLine($"  • Tools: POST http://localhost:{port}/mcp");
            Console.Error.WriteLine("📝 Example requests:");
            Console.Error.WriteLine("  • List tools: POST /mcp with {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
            Console.Error.WriteLine("  • Call tool: POST /mcp with {\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"compile_cpp\",\"arguments\":{\"sourceCode\":\"int main(){return 0;}\"}}}");
        }
    }

    private static int? GetPortFromArgs(ReadOnlySpan<string> args)
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
}