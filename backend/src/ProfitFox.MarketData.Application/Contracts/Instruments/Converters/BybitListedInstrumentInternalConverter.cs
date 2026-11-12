using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Contracts.Instruments.Converters;

public static class BybitListedInstrumentInternalConverter
{
    public static BybitListedInstrumentInternal ToInternal(this BybitListedInstrument listing)
    {
        return new BybitListedInstrumentInternal(
            listing.Category,
            listing.Symbol,
            listing.BaseAsset,
            listing.QuoteAsset,
            listing.Status,
            listing.Rules.ToInternal(),
            listing.ContractType,
            listing.LaunchTime);
    }
}