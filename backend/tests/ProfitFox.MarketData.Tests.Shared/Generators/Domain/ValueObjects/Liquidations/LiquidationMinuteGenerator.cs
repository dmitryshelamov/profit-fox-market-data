using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Liquidations;

public static class LiquidationMinuteGenerator
{
    /// <summary>
    ///     The liquidations of an instrument over the minute that starts at <paramref name="time" />: one long of 2 at 5 and no short.
    /// </summary>
    public static LiquidationMinute Generate(Guid instrumentId, DateTimeOffset time)
    {
        return new LiquidationMinute(instrumentId, time, new LiquidationSum(volume: 2m, turnover: 10m, count: 1), LiquidationSum.None);
    }
}