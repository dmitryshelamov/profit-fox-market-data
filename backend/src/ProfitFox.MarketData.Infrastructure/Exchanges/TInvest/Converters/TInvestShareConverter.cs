using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest.Converters;

/// <summary>
///     Turns the shares of the Invest API into listings of the domain: the price steps by the price increment, the quantity, in shares, by
///     the lot, and no order is below one lot.
/// </summary>
internal static class TInvestShareConverter
{
    private const string ClassCode = "TQBR";

    private const string Currency = "rub";

    /// <summary>
    ///     Whether the catalog takes the share: one of TQBR in roubles that trades through the API, with a positive lot and price step.
    ///     T-Invest lists the shares of other boards and currencies too.
    /// </summary>
    internal static bool IsListed(Share share)
    {
        return share.ClassCode == ClassCode
            && string.Equals(share.Currency, Currency, StringComparison.OrdinalIgnoreCase)
            && share.ApiTradeAvailableFlag
            && share.Lot > 0
            && PriceStep(share) > 0m;
    }

    internal static TInvestListedShare ToDomain(Share share)
    {
        return new TInvestListedShare(share.Uid, share.Ticker, new InstrumentRules(PriceStep(share), share.Lot, share.Lot));
    }

    private static decimal PriceStep(Share share)
    {
        return share.MinPriceIncrement?.ToDecimal() ?? 0m;
    }
}