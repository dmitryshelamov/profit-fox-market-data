using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Converters;

internal static class CandleSeriesDbConverter
{
    internal static CandleSeriesDb FromDomain(IReadOnlyCollection<CandleSeries> series)
    {
        (Guid InstrumentId, MinuteRange Range)[] covered =
            [.. series.SelectMany(one => one.Covered.Select(range => (one.InstrumentId, range)))];

        return new CandleSeriesDb
        {
            InstrumentIds = [.. series.Select(one => one.InstrumentId)],
            CoveredInstrumentIds = [.. covered.Select(range => range.InstrumentId)],
            CoveredFroms = [.. covered.Select(range => range.Range.From.ToUniversalTime())],
            CoveredUntils = [.. covered.Select(range => range.Range.Until.ToUniversalTime())]
        };
    }

    // The rows of one series; a series that covers nothing comes as one row with both ends null.
    internal static CandleSeries ToDomain(Guid instrumentId, IEnumerable<CoveredRangeDb> rows)
    {
        return CandleSeries.Restore(
            instrumentId,
            rows.Where(row => row.From is not null).Select(row => new MinuteRange(row.From!.Value, row.Until!.Value)));
    }

    internal static CandlesDb CandlesFromDomain(IReadOnlyCollection<CandleSeries> series)
    {
        (Guid InstrumentId, Candle Candle)[] candles =
            [.. series.SelectMany(one => one.NewCandles.Select(candle => (one.InstrumentId, candle)))];

        return new CandlesDb
        {
            InstrumentIds = [.. candles.Select(candle => candle.InstrumentId)],
            OpenTimes = [.. candles.Select(candle => candle.Candle.OpenTime.ToUniversalTime())],
            Opens = [.. candles.Select(candle => candle.Candle.Open)],
            Highs = [.. candles.Select(candle => candle.Candle.High)],
            Lows = [.. candles.Select(candle => candle.Candle.Low)],
            Closes = [.. candles.Select(candle => candle.Candle.Close)],
            Volumes = [.. candles.Select(candle => candle.Candle.Volume)],
            Turnovers = [.. candles.Select(candle => candle.Candle.Turnover)],
            BuyVolumes = [.. candles.Select(candle => candle.Candle.BuyVolume)]
        };
    }
}