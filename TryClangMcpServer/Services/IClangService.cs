using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TryClangMcpServer.Configuration;
using TryClangMcpServer.Models;

namespace TryClangMcpServer.Services;

/// <summary>
/// Service interface for Clang operations
/// </summary>
public interface IClangService
{
    /// <summary>
    /// Compiles C/C++ code with various options and returns diagnostics
    /// </summary>
    Task<ClangResult<CompilationResult>> CompileCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic");

    /// <summary>
    /// Performs static analysis using Clang Static Analyzer
    /// </summary>
    Task<ClangResult<AnalysisResult>> AnalyzeCppAsync(string sourceCode, string options = "-std=c++20 -Wall -Wextra -pedantic");

    /// <summary>
    /// Generates Abstract Syntax Trees in JSON format
    /// </summary>
    Task<ClangResult<AstResult>> GetAstAsync(string sourceCode, string options = "");

    /// <summary>
    /// Preprocesses C/C++ code and returns the expanded source
    /// </summary>
    Task<ClangResult<PreprocessResult>> PreprocessCppAsync(string sourceCode, string options = "-std=c++20", IReadOnlyDictionary<string, string>? definitions = null);
}

/// <summary>
/// Rate limiting service for HTTP endpoints
/// </summary>
public interface IRateLimitingService
{
    /// <summary>
    /// Checks if the client is allowed to make a request
    /// </summary>
    /// <param name="clientIdentifier">Client identifier (IP address, user ID, etc.)</param>
    /// <returns>True if allowed, false if rate limited</returns>
    Task<bool> IsAllowedAsync(string clientIdentifier);
    
    /// <summary>
    /// Gets the remaining quota for a client
    /// </summary>
    /// <param name="clientIdentifier">Client identifier</param>
    /// <returns>Number of remaining requests</returns>
    Task<int> GetRemainingQuotaAsync(string clientIdentifier);
}

/// <summary>
/// Simple in-memory rate limiting implementation
/// </summary>
public class InMemoryRateLimitingService : IRateLimitingService
{
    private readonly ConcurrentDictionary<string, ClientQuota> _clientQuotas = new();
    private readonly ClangOptions _options;
    private readonly ILogger<InMemoryRateLimitingService> _logger;
    private readonly Timer _cleanupTimer;

    public InMemoryRateLimitingService(IOptions<ClangOptions> options, ILogger<InMemoryRateLimitingService> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        // Cleanup expired entries every minute
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public Task<bool> IsAllowedAsync(string clientIdentifier)
    {
        if (string.IsNullOrEmpty(clientIdentifier))
            return Task.FromResult(false);

        var now = DateTime.UtcNow;
        var quota = _clientQuotas.AddOrUpdate(
            clientIdentifier,
            _ => new ClientQuota(now, 1, 1),
            (_, existing) => existing.TryConsume(now, _options.RateLimitRequestsPerMinute, _options.MaxRequestsPerHour)
        );

        var isAllowed = quota.CanMakeRequest;
        
        if (!isAllowed)
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId}. Minute: {MinuteCount}/{MinuteLimit}, Hour: {HourCount}/{HourLimit}",
                clientIdentifier, quota.MinuteCount, _options.RateLimitRequestsPerMinute, quota.HourCount, _options.MaxRequestsPerHour);
        }

        return Task.FromResult(isAllowed);
    }

    public Task<int> GetRemainingQuotaAsync(string clientIdentifier)
    {
        if (string.IsNullOrEmpty(clientIdentifier))
            return Task.FromResult(0);

        if (_clientQuotas.TryGetValue(clientIdentifier, out var quota))
        {
            var now = DateTime.UtcNow;
            quota.RefreshIfNeeded(now);
            return Task.FromResult(Math.Min(
                _options.RateLimitRequestsPerMinute - quota.MinuteCount,
                _options.MaxRequestsPerHour - quota.HourCount
            ));
        }

        return Task.FromResult(_options.RateLimitRequestsPerMinute);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var expiredKeys = _clientQuotas
            .Where(kvp => kvp.Value.LastRequest < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _clientQuotas.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired rate limiting entries", expiredKeys.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Represents the quota state for a client
/// </summary>
internal class ClientQuota
{
    private readonly object _lock = new();
    
    public DateTime LastRequest { get; private set; }
    public DateTime MinuteWindow { get; private set; }
    public DateTime HourWindow { get; private set; }
    public int MinuteCount { get; private set; }
    public int HourCount { get; private set; }
    public bool CanMakeRequest { get; private set; }

    public ClientQuota(DateTime now, int minuteCount, int hourCount)
    {
        LastRequest = now;
        MinuteWindow = now;
        HourWindow = now;
        MinuteCount = minuteCount;
        HourCount = hourCount;
        CanMakeRequest = true;
    }

    public ClientQuota TryConsume(DateTime now, int minuteLimit, int hourLimit)
    {
        lock (_lock)
        {
            RefreshIfNeeded(now);
            
            var newMinuteCount = MinuteCount + 1;
            var newHourCount = HourCount + 1;
            var canMakeRequest = newMinuteCount <= minuteLimit && newHourCount <= hourLimit;

            return new ClientQuota(now, newMinuteCount, newHourCount)
            {
                MinuteWindow = MinuteWindow,
                HourWindow = HourWindow,
                CanMakeRequest = canMakeRequest
            };
        }
    }

    public void RefreshIfNeeded(DateTime now)
    {
        // Reset minute window if more than 1 minute has passed
        if (now.Subtract(MinuteWindow).TotalMinutes >= 1)
        {
            MinuteWindow = now;
            MinuteCount = 0;
        }

        // Reset hour window if more than 1 hour has passed
        if (now.Subtract(HourWindow).TotalHours >= 1)
        {
            HourWindow = now;
            HourCount = 0;
        }
    }
}
