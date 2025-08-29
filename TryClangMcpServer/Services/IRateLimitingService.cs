namespace TryClangMcpServer.Services;

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
    ValueTask<bool> IsAllowedAsync(string clientIdentifier);

    /// <summary>
    /// Gets the remaining quota for a client
    /// </summary>
    /// <param name="clientIdentifier">Client identifier</param>
    /// <returns>Number of remaining requests</returns>
    ValueTask<int> GetRemainingQuotaAsync(string clientIdentifier);
}
