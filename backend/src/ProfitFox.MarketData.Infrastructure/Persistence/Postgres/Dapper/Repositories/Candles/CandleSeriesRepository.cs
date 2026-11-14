using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Converters;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles;

/// <summary>
///     Works on the connection of the scope and saves in a transaction of its own. A series is stored in <c>candle_series</c>,
///     its candles in the hypertable <c>candles</c>.
/// </summary>
internal sealed class CandleSeriesRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    PostgresTransactionExecutor transactionExecutor) : ICandleSeriesRepository
{
    public async Task<CandleSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        return (await GetAsync([instrumentId], cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyCollection<CandleSeries>> GetAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        IEnumerable<CoveredRangeDb> rows = await connection.QueryAsync<CoveredRangeDb>(
            CandleSeriesRepositoryQueries.Covered(instrumentIds, transactions.Current, cancellationToken));

        return [.. rows.GroupBy(row => row.InstrumentId).Select(series => CandleSeriesDbConverter.ToDomain(series.Key, series))];
    }

    public Task SaveAsync(CandleSeries series, CancellationToken cancellationToken)
    {
        return SaveAsync([series], cancellationToken);
    }

    // The candles and the coverage go together even outside a command, so the coverage never claims a minute storage does not hold.
    public async Task SaveAsync(IReadOnlyCollection<CandleSeries> series, CancellationToken cancellationToken)
    {
        if (series.Count == 0)
        {
            return;
        }

        CandleSeriesDb row = CandleSeriesDbConverter.FromDomain(series);
        CandlesDb candles = CandleSeriesDbConverter.CandlesFromDomain(series);

        await transactionExecutor.ExecuteAsync(
            async () =>
            {
                if (candles.OpenTimes.Length > 0)
                {
                    await connection.ExecuteAsync(CandleSeriesRepositoryQueries.UpsertCandles(candles, transactions.Current, cancellationToken));
                }

                await connection.ExecuteAsync(CandleSeriesRepositoryQueries.UpsertSeries(row, transactions.Current, cancellationToken));
            },
            cancellationToken);
    }
}