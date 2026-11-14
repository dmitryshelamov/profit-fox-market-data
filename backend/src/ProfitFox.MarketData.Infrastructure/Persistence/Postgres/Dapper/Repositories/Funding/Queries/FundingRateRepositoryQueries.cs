using System.Data;

using Dapper;

using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding.Queries;

internal static class FundingRateRepositoryQueries
{
    // The points go as one array per column, so a snapshot of the whole market is one statement. A stored point of the same
    // instrument and time takes the saved rate.
    private const string UpsertSql = """
                                     INSERT INTO funding_rates (instrument_id, time, rate)
                                     SELECT * FROM unnest(@InstrumentIds::uuid[], @Times::timestamptz[], @Rates::numeric[])
                                     ON CONFLICT (instrument_id, time) DO UPDATE SET
                                         rate = EXCLUDED.rate
                                     """;

    public static CommandDefinition Upsert(IReadOnlyCollection<FundingRatePoint> points, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        var columns = new
        {
            InstrumentIds = points.Select(point => point.InstrumentId).ToArray(),
            Times = points.Select(point => point.Time.ToUniversalTime()).ToArray(),
            Rates = points.Select(point => point.Rate).ToArray()
        };

        return new CommandDefinition(UpsertSql, columns, transaction, cancellationToken: cancellationToken);
    }
}