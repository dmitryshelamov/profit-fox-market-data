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
        return new Candle(
            candle.Time.ToDateTimeOffset(),
            candle.Open.ToDecimal(),
            candle.High.ToDecimal(),
            candle.Low.ToDecimal(),
            candle.Close.ToDecimal(),
            candle.Volume * lot,
            buyVolume: TellsSides(candle) ? candle.VolumeBuy * lot : null);
    }

    /// <summary>
    ///     A candle of the stream, closed or forming: the same values as a candle of the history.
    /// </summary>
    internal static Candle ToDomain(Tinkoff.InvestApi.V1.Candle candle, decimal lot)
    {
        return new Candle(
            candle.Time.ToDateTimeOffset(),
            candle.Open.ToDecimal(),
            candle.High.ToDecimal(),
            candle.Low.ToDecimal(),
            candle.Close.ToDecimal(),
            candle.Volume * lot,
            buyVolume: candle.VolumeBuy + candle.VolumeSell == candle.Volume ? candle.VolumeBuy * lot : null);
    }

    // T-Invest tells the sides of the trades from 26 August 2025: a candle before that has nothing bought and nothing sold whatever its
    // volume, which is not a minute of sells only.
    private static bool TellsSides(HistoricCandle candle)
    {
        return candle.VolumeBuy + candle.VolumeSell == candle.Volume;
    }
}