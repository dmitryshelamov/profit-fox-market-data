using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest;

/// <summary>
///     Works on the connection of the scope; Dapper opens a closed connection itself. The points are rows of the hypertable
///     <c>open_interest</c>, and a save is one statement, whole without a transaction of its own.
/// </summary>
internal sealed class OpenInterestRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOpenInterestRepository
{
    public Task SaveAsync(IReadOnlyCollection<OpenInterestPoint> points, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(OpenInterestRepositoryQueries.Upsert(points, transactions.Current, cancellationToken));
    }
}