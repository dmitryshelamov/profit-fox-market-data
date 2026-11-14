using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     What one subscriber of <see cref="IClosedCandleFeed" /> has waiting: the candles published since it subscribed, in the order they
///     were published.
/// </summary>
public interface IClosedCandleSubscription : IDisposable
{
    /// <summary>
    ///     The candles as they come, the waiting ones first; it ends only when cancelled or cut off.
    /// </summary>
    /// <exception cref="SubscriberFellBehindException">
    ///     More candles waited than a subscription holds: the subscriber is cut off after the ones that fitted.
    /// </exception>
    IAsyncEnumerable<ClosedCandle> ReadAllAsync(CancellationToken cancellationToken);
}