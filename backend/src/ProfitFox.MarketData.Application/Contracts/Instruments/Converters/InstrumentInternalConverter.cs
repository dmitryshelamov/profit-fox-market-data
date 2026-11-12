using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Contracts.Instruments.Converters;

public static class InstrumentInternalConverter
{
    public static InstrumentInternal ToInternal(this Instrument instrument)
    {
        return new InstrumentInternal(
            instrument.Id,
            instrument.Listing switch
            {
                BybitListedInstrument bybit => bybit.ToInternal(),
                _ => throw new NotSupportedException(
                    $"Instrument {instrument.Id} has a listing of {instrument.Listing.GetType().Name}, which the catalog does not show yet.")
            },
            instrument.FirstSeenAt,
            instrument.DelistedAt);
    }
}