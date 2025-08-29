using TryClangMcpServer.Configuration;
using TryClangMcpServer.HealthChecks;
using TryClangMcpServer.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TryClangMcpServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClangServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options with validation
        services.AddOptions<ClangOptions>()
            .Bind(configuration.GetSection(ClangOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register core services
        services.AddScoped<IClangService, ClangService>();
        services.AddSingleton<IRateLimitingService, InMemoryRateLimitingService>();

        // Add health checks
        services.AddHealthChecks()
            .AddCheck<ClangHealthCheck>("clang")
            .AddCheck("rate_limiting", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

        return services;
    }

    public static IServiceCollection AddModernLogging(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
            });
            builder.AddDebug();
        });

        return services;
    }

    public static IServiceCollection AddSecurityFeatures(this IServiceCollection services, IConfiguration configuration)
    {
        // Skip rate limiting configuration for now - can be added later if needed
        // The IRateLimitingService is already registered in AddClangServices

        return services;
    }
}