using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;

namespace ProfitFox.MarketData.Application.Contracts.LongShortRatio.Converters;

public static class InstrumentLongShortRatioInternalConverter
{
    public static InstrumentLongShortRatioInternal ToInternal(this LongShortRatioPoint point)
    {
        return new InstrumentLongShortRatioInternal(point.InstrumentId, point.Time, point.Long, point.Short);
    }
}