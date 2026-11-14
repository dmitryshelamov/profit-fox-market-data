using System.Data;

using Dapper;

using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest.Queries;

internal static class OpenInterestRepositoryQueries
{
    // The points go as one array per column, so a snapshot of the whole market is one statement. A stored point of the same
    // instrument and time takes the saved value.
    private const string UpsertSql = """
                                     INSERT INTO open_interest (instrument_id, time, value)
                                     SELECT * FROM unnest(@InstrumentIds::uuid[], @Times::timestamptz[], @Values::numeric[])
                                     ON CONFLICT (instrument_id, time) DO UPDATE SET
                                         value = EXCLUDED.value
                                     """;

    public static CommandDefinition Upsert(IReadOnlyCollection<OpenInterestPoint> points, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        var columns = new
        {
            InstrumentIds = points.Select(point => point.InstrumentId).ToArray(),
            Times = points.Select(point => point.Time.ToUniversalTime()).ToArray(),
            Values = points.Select(point => point.Value).ToArray()
        };

        return new CommandDefinition(UpsertSql, columns, transaction, cancellationToken: cancellationToken);
    }
}