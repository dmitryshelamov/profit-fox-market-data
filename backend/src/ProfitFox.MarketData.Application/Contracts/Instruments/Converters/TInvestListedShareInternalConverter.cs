using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Contracts.Instruments.Converters;

public static class TInvestListedShareInternalConverter
{
    public static TInvestListedShareInternal ToInternal(this TInvestListedShare share)
    {
        return new TInvestListedShareInternal(share.Uid, share.Ticker, share.Rules.ToInternal());
    }
}