using Google.Protobuf.WellKnownTypes;

using Tinkoff.InvestApi.V1;

// The SDK has a Candle of its own, the one of its stream.
using Candle = ProfitFox.MarketData.Domain.ValueObjects.Candles.Candle;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest.Converters;

/// <summary>
///     Turns the candles of the Invest API into candles of the domain: T-Invest tells the volume and how much of it was bought in lots, the
///     domain keeps both in shares, and tells no turnover.
/// </summary>
internal static class TInvestCandleConverter
{
    internal static Candle ToDomain(HistoricCandle candle, decimal lot)
    {
        return ToDomain(candle.Time, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.VolumeBuy, candle.VolumeSell, lot);
    }

    /// <summary>
    ///     A candle of the stream, closed or forming: the same values as a candle of the history.
    /// </summary>
    internal static Candle ToDomain(Tinkoff.InvestApi.V1.Candle candle, decimal lot)
    {
        return ToDomain(candle.Time, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.VolumeBuy, candle.VolumeSell, lot);
    }

    private static Candle ToDomain(
        Timestamp time,
        Quotation open,
        Quotation high,
        Quotation low,
        Quotation close,
        long lots,
        long lotsBought,
        long lotsSold,
        decimal lot)
    {
        // T-Invest tells the sides of the trades from 26 August 2025: a candle before that has nothing bought and nothing sold whatever
        // its volume, which is not a minute of sells only.
        bool tellsSides = lotsBought + lotsSold == lots;

        return new Candle(
            time.ToDateTimeOffset(),
            open.ToDecimal(),
            high.ToDecimal(),
            low.ToDecimal(),
            close.ToDecimal(),
            lots * lot,
            buyVolume: tellsSides ? lotsBought * lot : null);
    }
}