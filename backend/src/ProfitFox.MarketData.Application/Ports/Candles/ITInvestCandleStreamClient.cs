using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The minute candles of the shares of T-Invest as they close, each with how much of its volume the buyers took, and the candle of
///     the minute that goes on. T-Invest tells both itself; a minute without trades gets no candle of either kind.
/// </summary>
public interface ITInvestCandleStreamClient
{
    /// <summary>
    ///     Subscribes to the exchange candles of the shares, given by uid with the lot of each. From then on <paramref name="closed" />
    ///     gets the uid and the candle of every minute that closes, within a moment of its end, and <paramref name="forming" /> gets, at
    ///     most once a second for a share, the candle of the minute that goes on as T-Invest tells it, until the subscription is
    ///     disposed. The volumes are in shares. A stream that breaks or falls silent is opened and subscribed again by the client
    ///     itself; the minutes it missed meanwhile get no candle.
    /// </summary>
    /// <exception cref="NotSupportedException">TInvestApi:Token is not set; nothing is subscribed.</exception>
    Task<IAsyncDisposable> SubscribeAsync(
        IReadOnlyDictionary<string, decimal> lotsByUid,
        Action<string, Candle> closed,
        Action<string, Candle> forming,
        CancellationToken cancellationToken);
}