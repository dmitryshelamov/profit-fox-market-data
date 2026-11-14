using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Converters;

internal static class CandleSeriesDbConverter
{
    internal static CandleSeriesDb FromDomain(CandleSeries series)
    {
        return new CandleSeriesDb
        {
            InstrumentId = series.InstrumentId,
            CoveredFroms = [.. series.Covered.Select(range => range.From.ToUniversalTime())],
            CoveredUntils = [.. series.Covered.Select(range => range.Until.ToUniversalTime())]
        };
    }

    // A series that covers nothing comes as one row with both ends null.
    internal static CandleSeries ToDomain(Guid instrumentId, IEnumerable<CoveredRangeDb> rows)
    {
        return CandleSeries.Restore(
            instrumentId,
            rows.Where(row => row.From is not null).Select(row => new MinuteRange(row.From!.Value, row.Until!.Value)));
    }

    internal static CandlesDb CandlesFromDomain(CandleSeries series)
    {
        return new CandlesDb
        {
            InstrumentId = series.InstrumentId,
            OpenTimes = [.. series.NewCandles.Select(candle => candle.OpenTime.ToUniversalTime())],
            Opens = [.. series.NewCandles.Select(candle => candle.Open)],
            Highs = [.. series.NewCandles.Select(candle => candle.High)],
            Lows = [.. series.NewCandles.Select(candle => candle.Low)],
            Closes = [.. series.NewCandles.Select(candle => candle.Close)],
            Volumes = [.. series.NewCandles.Select(candle => candle.Volume)],
            Turnovers = [.. series.NewCandles.Select(candle => candle.Turnover)]
        };
    }
}