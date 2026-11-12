using ProfitFox.MarketData.Application.Contracts.Instruments.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

namespace ProfitFox.MarketData.Application.Contracts.Instruments.Converters;

public static class ListedInstrumentInternalConverter
{
    /// <summary>
    ///     The listing of the exchange it is of; a listing of an exchange the API does not show yet fails rather than show nothing.
    /// </summary>
    public static ListedInstrumentInternal ToInternal(this ListedInstrument listing)
    {
        return listing switch
        {
            BybitListedInstrument bybit => bybit.ToInternal(),
            TInvestListedShare share => share.ToInternal(),
            _ => throw new NotSupportedException($"The API does not show a listing of {listing.GetType().Name}.")
        };
    }
}