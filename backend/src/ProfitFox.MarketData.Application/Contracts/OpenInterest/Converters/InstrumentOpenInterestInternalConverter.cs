using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Application.Contracts.OpenInterest.Converters;

public static class InstrumentOpenInterestInternalConverter
{
    public static InstrumentOpenInterestInternal ToInternal(this OpenInterestPoint point)
    {
        return new InstrumentOpenInterestInternal(point.InstrumentId, point.Time, point.Value);
    }
}