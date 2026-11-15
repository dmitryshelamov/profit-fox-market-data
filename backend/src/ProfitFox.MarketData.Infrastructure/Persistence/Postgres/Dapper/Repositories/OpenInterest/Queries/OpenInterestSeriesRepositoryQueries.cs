using System.Data;

using Dapper;

using ProfitFox.MarketData.Domain.Entities.OpenInterest;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest.Queries;

/// <summary>
///     A series is a row of <c>open_interest_series</c>; its points are rows of <c>open_interest</c>, where the collection writes too.
/// </summary>
internal static class OpenInterestSeriesRepositoryQueries
{
    private const string SeriesSql = """
                                     SELECT instrument_id AS "InstrumentId", covered_from AS "CoveredFrom", covered_until AS "CoveredUntil"
                                     FROM open_interest_series
                                     WHERE instrument_id = @InstrumentId
                                     """;

    private const string CountPointsSql = """
                                          SELECT count(*)
                                          FROM open_interest
                                          WHERE instrument_id = @InstrumentId AND time >= @From AND time < @Until
                                          """;

    private const string UpsertSeriesSql = """
                                           INSERT INTO open_interest_series (instrument_id, covered_from, covered_until)
                                           VALUES (@InstrumentId, @CoveredFrom, @CoveredUntil)
                                           ON CONFLICT (instrument_id) DO UPDATE SET
                                               covered_from = EXCLUDED.covered_from,
                                               covered_until = EXCLUDED.covered_until
                                           """;

    // The points go as one array per column, so a page is one statement. A stored point stays: the collection keeps the value exactly
    // as the ticker told it, and the history may tell the same value rounded.
    private const string InsertPointsSql = """
                                           INSERT INTO open_interest (instrument_id, time, value)
                                           SELECT @InstrumentId, p.time, p.value
                                           FROM unnest(@Times::timestamptz[], @Values::numeric[]) AS p(time, value)
                                           ON CONFLICT (instrument_id, time) DO NOTHING
                                           """;

    public static CommandDefinition Series(Guid instrumentId, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SeriesSql, new { InstrumentId = instrumentId }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition CountPoints(
        Guid instrumentId,
        DateTimeOffset from,
        DateTimeOffset until,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            CountPointsSql,
            new { InstrumentId = instrumentId, From = from.ToUniversalTime(), Until = until.ToUniversalTime() },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertSeries(OpenInterestSeries series, IDbTransaction? transaction, CancellationToken cancellationToken)
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

    public static CommandDefinition InsertPoints(OpenInterestSeries series, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InsertPointsSql,
            new
            {
                series.InstrumentId,
                Times = series.NewPoints.Select(point => point.Time.ToUniversalTime()).ToArray(),
                Values = series.NewPoints.Select(point => point.Value).ToArray()
            },
            transaction,
            cancellationToken: cancellationToken);
    }
}