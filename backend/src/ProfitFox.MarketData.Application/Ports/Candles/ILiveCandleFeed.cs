using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The candles as they come, for whoever follows them live: the closed ones once they are saved and the forming ones of the minute
///     that goes on. The feed keeps nothing: a subscriber gets what is published while it is subscribed, and reads the closed candles
///     it missed from storage.
/// </summary>
public interface ILiveCandleFeed
{
    /// <summary>
    ///     Tells the candles to every subscriber. A closed candle is published once it is saved, so a subscriber never hears of one
    ///     storage does not hold.
    /// </summary>
    void Publish(IReadOnlyCollection<LiveCandle> candles);

    /// <summary>
    ///     Starts to gather what is published from now on for a new subscriber, until the subscription is disposed: the closed candles
    ///     of <paramref name="exchanges" />, and with <paramref name="forming" /> their forming ones too. Candles of other exchanges
    ///     never wait for the subscriber.
    /// </summary>
    ILiveCandleSubscription Subscribe(IReadOnlyCollection<Exchange> exchanges, bool forming);
}