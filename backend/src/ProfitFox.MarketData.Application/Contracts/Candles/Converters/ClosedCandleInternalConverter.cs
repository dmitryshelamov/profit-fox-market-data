using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;

namespace ProfitFox.MarketData.Application.Contracts.Candles.Converters;

public static class ClosedCandleInternalConverter
{
    public static ClosedCandleInternal ToInternal(this ClosedCandle closed)
    {
        return new ClosedCandleInternal(
            closed.Exchange,
            closed.InstrumentId,
            closed.Candle.OpenTime,
            closed.Candle.Open,
            closed.Candle.High,
            closed.Candle.Low,
            closed.Candle.Close,
            closed.Candle.Volume,
            closed.Candle.Turnover,
            closed.Candle.BuyVolume);
    }
}