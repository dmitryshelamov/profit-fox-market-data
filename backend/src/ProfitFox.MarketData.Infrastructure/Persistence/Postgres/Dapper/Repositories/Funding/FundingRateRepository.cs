using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding;

/// <summary>
///     Works on the connection of the scope; Dapper opens a closed connection itself. The points are rows of the hypertable
///     <c>funding_rates</c>, and a save is one statement, whole without a transaction of its own.
/// </summary>
internal sealed class FundingRateRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IFundingRateRepository
{
    public Task SaveAsync(IReadOnlyCollection<FundingRatePoint> points, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(FundingRateRepositoryQueries.Upsert(points, transactions.Current, cancellationToken));
    }
}