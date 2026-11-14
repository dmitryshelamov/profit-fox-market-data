using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The closed candles as they are saved, for whoever follows them live. The feed keeps nothing: a subscriber gets what is published
///     while it is subscribed, and reads what it missed from storage.
/// </summary>
public interface IClosedCandleFeed
{
    /// <summary>
    ///     Tells the candles to every subscriber; called once they are saved, so a subscriber never hears of a candle storage does not hold.
    /// </summary>
    void Publish(IReadOnlyCollection<ClosedCandle> candles);

    /// <summary>
    ///     Starts to gather what is published from now on for a new subscriber, until the subscription is disposed.
    /// </summary>
    IClosedCandleSubscription Subscribe();
}