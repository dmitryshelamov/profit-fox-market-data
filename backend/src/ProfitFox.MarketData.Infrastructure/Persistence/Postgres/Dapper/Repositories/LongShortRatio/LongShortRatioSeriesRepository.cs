using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.LongShortRatio.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.LongShortRatio.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.LongShortRatio;

/// <summary>
///     Works on the connection of the scope and saves in a transaction of its own. A series is stored in
///     <c>long_short_ratio_series</c>, its points in the hypertable <c>long_short_ratio</c>.
/// </summary>
internal sealed class LongShortRatioSeriesRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    PostgresTransactionExecutor transactionExecutor) : ILongShortRatioSeriesRepository
{
    public async Task<LongShortRatioSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        LongShortRatioSeriesDb? row = await connection.QuerySingleOrDefaultAsync<LongShortRatioSeriesDb>(
            LongShortRatioSeriesRepositoryQueries.Series(instrumentId, transactions.Current, cancellationToken));

        return row is null ? null : LongShortRatioSeries.Restore(row.InstrumentId, row.CoveredFrom, row.CoveredUntil);
    }

    // The points and the coverage go together, so the coverage never claims a time storage does not hold.
    public Task SaveAsync(LongShortRatioSeries series, CancellationToken cancellationToken)
    {
        return transactionExecutor.ExecuteAsync(
            async () =>
            {
                if (series.NewPoints.Count > 0)
                {
                    await connection.ExecuteAsync(LongShortRatioSeriesRepositoryQueries.InsertPoints(series, transactions.Current, cancellationToken));
                }

                await connection.ExecuteAsync(LongShortRatioSeriesRepositoryQueries.UpsertSeries(series, transactions.Current, cancellationToken));
            },
            cancellationToken);
    }
}