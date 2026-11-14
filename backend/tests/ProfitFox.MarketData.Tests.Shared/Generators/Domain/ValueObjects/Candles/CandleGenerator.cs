using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;

public static class CandleGenerator
{
    /// <summary>
    ///     A valid minute candle that opens at <paramref name="openTime" />; a test passes only the values it checks.
    /// </summary>
    public static Candle Generate(DateTimeOffset openTime, decimal close = 5m, decimal volume = 1m, decimal? turnover = null)
    {
        return new Candle(openTime, open: 5m, high: 10m, low: 1m, close, volume, turnover);
    }
}