namespace ProfitFox.MarketData.Tests.Integration.Http;

/// <summary>
///     Answers of the Bybit v5 market API as Bybit writes them, for <see cref="StubHttpHandler" />.
/// </summary>
internal static class BybitAnswers
{
    public static string SpotPair(string symbol, string baseCoin, string quoteCoin, string tickSize, string basePrecision, string minOrderQty, string minOrderAmt)
    {
        return $$$"""
                  {"symbol":"{{{symbol}}}","baseCoin":"{{{baseCoin}}}","quoteCoin":"{{{quoteCoin}}}","status":"Trading",
                   "lotSizeFilter":{"basePrecision":"{{{basePrecision}}}","quotePrecision":"0.00000001","minOrderQty":"{{{minOrderQty}}}","maxOrderQty":"1000","minOrderAmt":"{{{minOrderAmt}}}","maxOrderAmt":"2000000"},
                   "priceFilter":{"tickSize":"{{{tickSize}}}"}}
                  """;
    }

    public static string LinearContract(
        string symbol,
        string baseCoin,
        string contractType = "LinearPerpetual",
        string status = "Trading")
    {
        return $$$"""
                  {"symbol":"{{{symbol}}}","contractType":"{{{contractType}}}","status":"{{{status}}}","baseCoin":"{{{baseCoin}}}","quoteCoin":"USDT","settleCoin":"USDT",
                   "launchTime":"1585526400000","deliveryTime":"0",
                   "priceFilter":{"minPrice":"0.10","maxPrice":"1999999.80","tickSize":"0.10"},
                   "lotSizeFilter":{"maxOrderQty":"1190.000","minOrderQty":"0.001","qtyStep":"0.001","minNotionalValue":"5"}}
                  """;
    }

    public static string List(string category, string nextPageCursor = "", params string[] items)
    {
        return $$"""
                 {"retCode":0,"retMsg":"OK","result":{"category":"{{category}}","list":[{{string.Join(",", items)}}],"nextPageCursor":"{{nextPageCursor}}"},"retExtInfo":{},"time":0}
                 """;
    }

    public static string Error(int code, string message)
    {
        return $$"""{"retCode":{{code}},"retMsg":"{{message}}","result":{},"retExtInfo":{},"time":0}""";
    }
}