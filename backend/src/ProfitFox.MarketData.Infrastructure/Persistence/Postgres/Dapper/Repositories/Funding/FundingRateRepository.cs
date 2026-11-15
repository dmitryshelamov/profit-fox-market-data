using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding;

/// <summary>
///     Works on the connection of the scope; Dapper opens a closed connection itself. The points are rows of the hypertable
///     <c>funding_rates</c>, and a save is one statement, so it is whole without a transaction.
/// </summary>
internal sealed class FundingRateRepository(NpgsqlConnection connection) : IFundingRateRepository
{
    public Task SaveAsync(IReadOnlyCollection<FundingRatePoint> points, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(FundingRateRepositoryQueries.Upsert(points, cancellationToken));
    }
}