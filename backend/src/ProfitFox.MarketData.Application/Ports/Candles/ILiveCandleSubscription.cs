using ProfitFox.MarketData.Application.Ports.Feeds;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     What one subscriber of <see cref="ILiveCandleFeed" /> has waiting: the candles of its exchanges published since it subscribed,
///     in the order they were published. A forming candle is left out for a subscriber with many candles waiting: the next one of its
///     minute makes it stale anyway. A closed candle is never left out: a subscriber with more closed candles waiting than a
///     subscription holds is cut off after the ones that fitted.
/// </summary>
public interface ILiveCandleSubscription : ILiveSubscription<LiveCandle>;