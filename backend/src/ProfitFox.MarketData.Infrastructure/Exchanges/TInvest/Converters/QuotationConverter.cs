using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest.Converters;

internal static class QuotationConverter
{
    // A Quotation is units + nano / 1e9.
    private const decimal NanosInUnit = 1_000_000_000m;

    internal static decimal ToDecimal(this Quotation quotation)
    {
        return quotation.Units + quotation.Nano / NanosInUnit;
    }
}