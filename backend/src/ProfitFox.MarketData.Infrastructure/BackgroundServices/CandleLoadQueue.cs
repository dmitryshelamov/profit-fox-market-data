namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The windows reads asked for and could not load in time, which <see cref="CandleSyncBackgroundService" /> loads before the history;
///     the same window is queued once. The queue is kept in memory only: after a restart a read asks again.
/// </summary>
internal sealed class CandleLoadQueue
{
    private readonly Lock _lock = new Lock();
    private readonly List<CandleLoadRequest> _requests = [];

    public void Enqueue(CandleLoadRequest request)
    {
        lock (_lock)
        {
            if (!_requests.Contains(request))
            {
                _requests.Add(request);
            }
        }
    }

    /// <summary>
    ///     The queued windows, oldest first.
    /// </summary>
    public IReadOnlyList<CandleLoadRequest> Pending()
    {
        lock (_lock)
        {
            return [.. _requests];
        }
    }

    /// <summary>
    ///     Takes a window out once nothing of it is left to load.
    /// </summary>
    public void Complete(CandleLoadRequest request)
    {
        lock (_lock)
        {
            _requests.Remove(request);
        }
    }
}