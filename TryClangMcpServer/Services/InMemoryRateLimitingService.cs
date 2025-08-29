using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TryClangMcpServer.Configuration;

namespace TryClangMcpServer.Services;

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

    public ValueTask<bool> IsAllowedAsync(string clientIdentifier)
    {
        if (string.IsNullOrEmpty(clientIdentifier))
            return ValueTask.FromResult(false);

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

        return ValueTask.FromResult(isAllowed);
    }

    public ValueTask<int> GetRemainingQuotaAsync(string clientIdentifier)
    {
        if (string.IsNullOrEmpty(clientIdentifier))
            return ValueTask.FromResult(0);

        if (_clientQuotas.TryGetValue(clientIdentifier, out var quota))
        {
            var now = DateTime.UtcNow;
            quota.RefreshIfNeeded(now);
            return ValueTask.FromResult(Math.Min(
                _options.RateLimitRequestsPerMinute - quota.MinuteCount,
                _options.MaxRequestsPerHour - quota.HourCount
            ));
        }

        return ValueTask.FromResult(_options.RateLimitRequestsPerMinute);
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
