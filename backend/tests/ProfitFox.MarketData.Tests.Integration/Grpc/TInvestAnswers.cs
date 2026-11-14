using Google.Protobuf.WellKnownTypes;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Tests.Integration.Grpc;

/// <summary>
///     Answers of the Invest API of T-Invest, for <see cref="StubGrpcInvoker" />: by default a share of TQBR in roubles that trades through
///     the API, with a lot of 10 and a price step of a kopeck.
/// </summary>
internal static class TInvestAnswers
{
    private const decimal NanosInUnit = 1_000_000_000m;

    public static SharesResponse Shares(params Share[] shares)
    {
        return new SharesResponse { Instruments = { shares } };
    }

    public static Share Share(
        string ticker,
        string uid,
        int lot = 10,
        decimal priceStep = 0.01m,
        string classCode = "TQBR",
        string currency = "rub",
        bool apiTrade = true)
    {
        return new Share
        {
            Ticker = ticker,
            Uid = uid,
            Lot = lot,
            ClassCode = classCode,
            Currency = currency,
            ApiTradeAvailableFlag = apiTrade,
            MinPriceIncrement = Quotation(priceStep)
        };
    }

    public static GetCandlesResponse Candles(params HistoricCandle[] candles)
    {
        return new GetCandlesResponse { Candles = { candles } };
    }

    /// <summary>
    ///     A minute candle of the exchange, its volume in lots. With <paramref name="lotsBought" /> it tells the sides of its trades, the
    ///     rest of the volume sold; without, it tells none, as the candles before T-Invest began to.
    /// </summary>
    public static HistoricCandle Candle(
        DateTimeOffset time,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long lots,
        bool complete = true,
        long? lotsBought = null)
    {
        return new HistoricCandle
        {
            Time = Timestamp.FromDateTimeOffset(time),
            Open = Quotation(open),
            High = Quotation(high),
            Low = Quotation(low),
            Close = Quotation(close),
            Volume = lots,
            VolumeBuy = lotsBought ?? 0,
            VolumeSell = lotsBought is null ? 0 : lots - lotsBought.Value,
            IsComplete = complete,
            CandleSource = CandleSource.Exchange
        };
    }

    public static ShareResponse ShareBy(string uid, DateTimeOffset? firstMinuteCandle)
    {
        var share = new Share { Uid = uid };

        if (firstMinuteCandle is { } first)
        {
            share.First1MinCandleDate = Timestamp.FromDateTimeOffset(first);
        }

        return new ShareResponse { Instrument = share };
    }

    private static Quotation Quotation(decimal value)
    {
        var units = decimal.Truncate(value);

        return new Quotation { Units = (long)units, Nano = (int)((value - units) * NanosInUnit) };
    }
}