using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding;

/// <summary>
///     Works on the connection of the scope and saves in a transaction of its own. A series is stored in
///     <c>funding_payment_series</c>, its payments in <c>funding_payments</c>.
/// </summary>
internal sealed class FundingPaymentSeriesRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    PostgresTransactionExecutor transactionExecutor) : IFundingPaymentSeriesRepository
{
    public async Task<FundingPaymentSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        FundingPaymentSeriesDb? row = await connection.QuerySingleOrDefaultAsync<FundingPaymentSeriesDb>(
            FundingPaymentSeriesRepositoryQueries.Series(instrumentId, transactions.Current, cancellationToken));

        return row is null ? null : FundingPaymentSeries.Restore(row.InstrumentId, row.CoveredFrom, row.CoveredUntil);
    }

    // The payments and the coverage go together, so the coverage never claims a time storage does not hold.
    public Task SaveAsync(FundingPaymentSeries series, CancellationToken cancellationToken)
    {
        return transactionExecutor.ExecuteAsync(
            async () =>
            {
                if (series.NewPayments.Count > 0)
                {
                    await connection.ExecuteAsync(
                        FundingPaymentSeriesRepositoryQueries.UpsertPayments(series, transactions.Current, cancellationToken));
                }

                await connection.ExecuteAsync(FundingPaymentSeriesRepositoryQueries.UpsertSeries(series, transactions.Current, cancellationToken));
            },
            cancellationToken);
    }
}