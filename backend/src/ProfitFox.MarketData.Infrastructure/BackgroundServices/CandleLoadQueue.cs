using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The ranges reads asked for and could not wait for, which <see cref="CandleSyncBackgroundService" /> loads before the history. The
///     same range is queued once, and at most <see cref="Capacity" /> wait: one more is not taken, and its reader asks again later. The
///     queue is kept in memory only, so after a restart a read asks again too.
/// </summary>
internal sealed class CandleLoadQueue : ICandleLoadQueue
{
    private const int Capacity = 1000;

    private readonly Lock _lock = new Lock();
    private readonly List<CandleLoadRequest> _requests = [];

    public void Enqueue(Guid instrumentId, MinuteRange range)
    {
        var request = new CandleLoadRequest(instrumentId, range);

        lock (_lock)
        {
            if (_requests.Count < Capacity && !_requests.Contains(request))
            {
                _requests.Add(request);
            }
        }
    }

    /// <summary>
    ///     The queued ranges, oldest first.
    /// </summary>
    public IReadOnlyList<CandleLoadRequest> Pending()
    {
        lock (_lock)
        {
            return [.. _requests];
        }
    }

    /// <summary>
    ///     Takes a range out: nothing of it is left to load, or it failed and its reader asks again.
    /// </summary>
    public void Remove(CandleLoadRequest request)
    {
        lock (_lock)
        {
            _requests.Remove(request);
        }
    }
}