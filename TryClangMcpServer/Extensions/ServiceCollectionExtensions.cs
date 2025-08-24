using TryClangMcpServer.Configuration;
using TryClangMcpServer.Services;

namespace TryClangMcpServer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClangServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options
        services.Configure<ClangOptions>(configuration.GetSection(ClangOptions.SectionName));
        
        // Register services
        services.AddScoped<IClangService, ClangService>();
        
        return services;
    }
}