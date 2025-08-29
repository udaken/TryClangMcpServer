namespace TryClangMcpServer.Services;

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
