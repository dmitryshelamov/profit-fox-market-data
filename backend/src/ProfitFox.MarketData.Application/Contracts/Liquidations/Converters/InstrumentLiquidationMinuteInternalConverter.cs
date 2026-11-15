using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Application.Contracts.Liquidations.Converters;

public static class InstrumentLiquidationMinuteInternalConverter
{
    public static InstrumentLiquidationMinuteInternal ToInternal(this StoredLiquidationMinute stored)
    {
        return new InstrumentLiquidationMinuteInternal(
            stored.Minute.InstrumentId,
            stored.Minute.Time,
            stored.Minute.Longs.ToInternal(),
            stored.Minute.Shorts.ToInternal(),
            stored.IsValid);
    }

    private static LiquidationSumInternal ToInternal(this LiquidationSum sum)
    {
        return new LiquidationSumInternal(sum.Volume, sum.Turnover, sum.Count);
    }
}