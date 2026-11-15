using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest;

/// <summary>
///     Works on the connection of the scope and saves in a transaction of its own. A series is stored in <c>open_interest_series</c>,
///     its points in the hypertable <c>open_interest</c>.
/// </summary>
internal sealed class OpenInterestSeriesRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    PostgresTransactionExecutor transactionExecutor) : IOpenInterestSeriesRepository
{
    public async Task<OpenInterestSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        OpenInterestSeriesDb? row = await connection.QuerySingleOrDefaultAsync<OpenInterestSeriesDb>(
            OpenInterestSeriesRepositoryQueries.Series(instrumentId, transactions.Current, cancellationToken));

        return row is null ? null : OpenInterestSeries.Restore(row.InstrumentId, row.CoveredFrom, row.CoveredUntil);
    }

    public Task<int> CountPointsAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken)
    {
        return connection.ExecuteScalarAsync<int>(
            OpenInterestSeriesRepositoryQueries.CountPoints(instrumentId, from, until, transactions.Current, cancellationToken));
    }

    // The points and the coverage go together, so the coverage never claims a time storage does not hold.
    public Task SaveAsync(OpenInterestSeries series, CancellationToken cancellationToken)
    {
        return transactionExecutor.ExecuteAsync(
            async () =>
            {
                if (series.NewPoints.Count > 0)
                {
                    await connection.ExecuteAsync(OpenInterestSeriesRepositoryQueries.InsertPoints(series, transactions.Current, cancellationToken));
                }

                await connection.ExecuteAsync(OpenInterestSeriesRepositoryQueries.UpsertSeries(series, transactions.Current, cancellationToken));
            },
            cancellationToken);
    }
}