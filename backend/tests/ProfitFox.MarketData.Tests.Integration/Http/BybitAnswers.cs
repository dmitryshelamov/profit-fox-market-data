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

    /// <summary>
    ///     A ticker of a linear contract as Bybit writes it: the open interest in the base coin and the funding rate of the coming
    ///     payment, which is empty for a dated futures contract.
    /// </summary>
    public static string LinearTicker(string symbol, string openInterest, string fundingRate)
    {
        return $$$"""
                  {"symbol":"{{{symbol}}}","lastPrice":"67000.10","indexPrice":"67010.55","markPrice":"67001.20","prevPrice24h":"66000.00","price24hPcnt":"0.0151",
                   "highPrice24h":"67500.00","lowPrice24h":"65800.00","prevPrice1h":"66950.00","openInterest":"{{{openInterest}}}","openInterestValue":"3507133763.03",
                   "turnover24h":"5000000000.00","volume24h":"75000.000","fundingRate":"{{{fundingRate}}}","nextFundingTime":"1794672000000",
                   "predictedDeliveryPrice":"","basisRate":"","deliveryFeeRate":"","deliveryTime":"0","ask1Size":"1.500","bid1Price":"67000.00",
                   "ask1Price":"67000.10","bid1Size":"2.300","basis":"","preOpenPrice":"","preQty":"","curPreListingPhase":"","fundingIntervalHour":"8",
                   "fundingCap":"0.005","basisRateYear":""}
                  """;
    }

    public static string Tickers(string category, params string[] tickers)
    {
        return $$"""
                 {"retCode":0,"retMsg":"OK","result":{"category":"{{category}}","list":[{{string.Join(",", tickers)}}]},"retExtInfo":{},"time":0}
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

    /// <summary>
    ///     A kline as Bybit writes it: the start in milliseconds, then open, high, low, close, volume and turnover.
    /// </summary>
    public static string Kline(DateTimeOffset start, string open, string high, string low, string close, string volume, string turnover)
    {
        return $"""["{start.ToUnixTimeMilliseconds()}","{open}","{high}","{low}","{close}","{volume}","{turnover}"]""";
    }

    public static string Klines(string category, string symbol, params string[] klines)
    {
        return $$"""
                 {"retCode":0,"retMsg":"OK","result":{"category":"{{category}}","symbol":"{{symbol}}","list":[{{string.Join(",", klines)}}]},"retExtInfo":{},"time":0}
                 """;
    }
}