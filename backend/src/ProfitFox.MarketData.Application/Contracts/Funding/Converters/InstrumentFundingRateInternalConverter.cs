using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Application.Contracts.Funding.Converters;

public static class InstrumentFundingRateInternalConverter
{
    public static InstrumentFundingRateInternal ToInternal(this FundingRatePoint point)
    {
        return new InstrumentFundingRateInternal(point.InstrumentId, point.Time, point.Rate);
    }
}