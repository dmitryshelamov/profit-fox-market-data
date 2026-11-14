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
        (Guid InstrumentId, MinuteRange Range)[] settled =
            [.. series.SelectMany(one => one.Settled.Select(range => (one.InstrumentId, range)))];

        return new CandleSeriesDb
        {
            InstrumentIds = [.. series.Select(one => one.InstrumentId)],
            CoveredInstrumentIds = [.. covered.Select(range => range.InstrumentId)],
            CoveredFroms = [.. covered.Select(range => range.Range.From.ToUniversalTime())],
            CoveredUntils = [.. covered.Select(range => range.Range.Until.ToUniversalTime())],
            SettledInstrumentIds = [.. settled.Select(range => range.InstrumentId)],
            SettledFroms = [.. settled.Select(range => range.Range.From.ToUniversalTime())],
            SettledUntils = [.. settled.Select(range => range.Range.Until.ToUniversalTime())]
        };
    }

    // The rows of one series, oldest first; a series that covers nothing comes as one row with both ends null.
    internal static CandleSeries ToDomain(Guid instrumentId, IEnumerable<SeriesRangeDb> rows)
    {
        ILookup<bool, MinuteRange> ranges = rows
            .Where(row => row.From is not null)
            .ToLookup(row => row.Settled, row => new MinuteRange(row.From!.Value, row.Until!.Value));

        return CandleSeries.Restore(instrumentId, covered: ranges[false], settled: ranges[true]);
    }

    internal static Candle ToDomain(CandleDb row)
    {
        return new Candle(row.OpenTime, row.Open, row.High, row.Low, row.Close, row.Volume, row.Turnover, row.BuyVolume);
    }

    // The candles of many instruments, each under its instrument.
    internal static ILookup<Guid, Candle> ToDomain(IEnumerable<CandleDb> rows)
    {
        return rows.ToLookup(row => row.InstrumentId, ToDomain);
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