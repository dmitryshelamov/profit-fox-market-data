using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     What one subscriber of <see cref="ILiveCandleFeed" /> has waiting: the candles of its exchanges published since it subscribed,
///     in the order they were published. A forming candle is left out for a subscriber with many candles waiting: the next one of its
///     minute makes it stale anyway. A closed candle is never left out.
/// </summary>
public interface ILiveCandleSubscription : IDisposable
{
    /// <summary>
    ///     The candles as they come, the waiting ones first; it ends once the subscription is disposed and the waiting ones are read,
    ///     or when cancelled or cut off.
    /// </summary>
    /// <exception cref="SubscriberFellBehindException">
    ///     More closed candles waited than a subscription holds: the subscriber is cut off after the ones that fitted.
    /// </exception>
    IAsyncEnumerable<LiveCandle> ReadAllAsync(CancellationToken cancellationToken);
}