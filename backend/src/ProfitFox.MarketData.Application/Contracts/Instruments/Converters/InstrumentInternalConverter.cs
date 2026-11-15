using ProfitFox.MarketData.Domain.Entities.Instruments;

namespace ProfitFox.MarketData.Application.Contracts.Instruments.Converters;

public static class InstrumentInternalConverter
{
    public static InstrumentInternal ToInternal(this Instrument instrument)
    {
        return new InstrumentInternal(
            instrument.Id,
            instrument.Listing.ToInternal(),
            instrument.FirstSeenAt,
            instrument.DelistedAt);
    }
}