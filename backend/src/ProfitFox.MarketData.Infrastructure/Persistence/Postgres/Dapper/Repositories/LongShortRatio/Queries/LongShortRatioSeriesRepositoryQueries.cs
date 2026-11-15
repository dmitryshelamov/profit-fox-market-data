using System.Data;

using Dapper;

using ProfitFox.MarketData.Domain.Entities.LongShortRatio;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.LongShortRatio.Queries;

/// <summary>
///     A series is a row of <c>long_short_ratio_series</c>; its points are rows of <c>long_short_ratio</c>.
/// </summary>
internal static class LongShortRatioSeriesRepositoryQueries
{
    private const string SeriesSql = """
                                     SELECT instrument_id AS "InstrumentId", covered_from AS "CoveredFrom", covered_until AS "CoveredUntil"
                                     FROM long_short_ratio_series
                                     WHERE instrument_id = @InstrumentId
                                     """;

    private const string UpsertSeriesSql = """
                                           INSERT INTO long_short_ratio_series (instrument_id, covered_from, covered_until)
                                           VALUES (@InstrumentId, @CoveredFrom, @CoveredUntil)
                                           ON CONFLICT (instrument_id) DO UPDATE SET
                                               covered_from = EXCLUDED.covered_from,
                                               covered_until = EXCLUDED.covered_until
                                           """;

    // The points go as one array per column, so a page is one statement. A stored point stays: the exchange does not change a point
    // it has told.
    private const string InsertPointsSql = """
                                           INSERT INTO long_short_ratio (instrument_id, time, long, short)
                                           SELECT @InstrumentId, p.time, p.long, p.short
                                           FROM unnest(@Times::timestamptz[], @Longs::numeric[], @Shorts::numeric[]) AS p(time, long, short)
                                           ON CONFLICT (instrument_id, time) DO NOTHING
                                           """;

    public static CommandDefinition Series(Guid instrumentId, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SeriesSql, new { InstrumentId = instrumentId }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertSeries(LongShortRatioSeries series, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            UpsertSeriesSql,
            new
            {
                series.InstrumentId,
                CoveredFrom = series.CoveredFrom.ToUniversalTime(),
                CoveredUntil = series.CoveredUntil.ToUniversalTime()
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition InsertPoints(LongShortRatioSeries series, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertPointsSql,
            new
            {
                series.InstrumentId,
                Times = series.NewPoints.Select(point => point.Time.ToUniversalTime()).ToArray(),
                Longs = series.NewPoints.Select(point => point.Long).ToArray(),
                Shorts = series.NewPoints.Select(point => point.Short).ToArray()
            },
            transaction,
            cancellationToken: cancellationToken);
    }
}