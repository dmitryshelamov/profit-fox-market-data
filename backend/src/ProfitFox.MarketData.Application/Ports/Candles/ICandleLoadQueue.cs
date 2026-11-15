using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The ranges a read asked for and could not wait for, which the background loading takes before the history.
/// </summary>
public interface ICandleLoadQueue
{
    void Enqueue(Guid instrumentId, MinuteRange range);
}