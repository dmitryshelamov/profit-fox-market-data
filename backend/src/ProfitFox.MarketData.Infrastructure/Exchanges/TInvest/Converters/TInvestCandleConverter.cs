using Tinkoff.InvestApi.V1;

// The SDK has a Candle of its own, the one of its stream.
using Candle = ProfitFox.MarketData.Domain.ValueObjects.Candles.Candle;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest.Converters;

/// <summary>
///     Turns the candles of the Invest API into candles of the domain: T-Invest tells the volume in lots, the domain keeps it in shares,
///     and tells no turnover.
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
            candle.Volume * lot);
    }
}