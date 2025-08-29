using Microsoft.Extensions.Diagnostics.HealthChecks;
using TryClangMcpServer.Services;

namespace TryClangMcpServer.HealthChecks;

public class ClangHealthCheck(IClangService clangService) : IHealthCheck
{
    private const string TestCode = "#include <iostream>\nint main(){return 0;}";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await clangService.CompileCppAsync(TestCode, "-std=c++17");

            var data = new Dictionary<string, object>
            {
                ["test_compilation"] = result != null ? "success" : "failed",
                ["timestamp"] = DateTime.UtcNow
            };

            return result != null
                ? HealthCheckResult.Healthy("Clang compilation service is operational", data)
                : HealthCheckResult.Degraded("Clang compilation returned null result", null, data);
        }
        catch (Exception ex)
        {
            var data = new Dictionary<string, object>
            {
                ["error"] = ex.Message,
                ["timestamp"] = DateTime.UtcNow
            };

            return HealthCheckResult.Unhealthy("Clang compilation service failed", ex, data);
        }
    }
}