using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations;

/// <summary>
///     Works on the connection of the scope; Dapper opens a closed connection itself. The minutes are rows of the hypertable
///     <c>liquidations</c>, and each call is one statement, so it is whole without a transaction.
/// </summary>
internal sealed class LiquidationRepository(NpgsqlConnection connection) : ILiquidationRepository
{
    public Task AddAsync(IReadOnlyCollection<LiquidationMinute> minutes, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(LiquidationRepositoryQueries.Add(minutes, cancellationToken));
    }

    public Task InvalidateAsync(IReadOnlyCollection<Guid> instrumentIds, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(LiquidationRepositoryQueries.Invalidate(instrumentIds, from, until, cancellationToken));
    }

    public async Task<DateTimeOffset?> GetLastTimeAsync(CancellationToken cancellationToken)
    {
        DateTime? last = await connection.ExecuteScalarAsync<DateTime?>(LiquidationRepositoryQueries.LastTime(cancellationToken));

        return last is null ? null : new DateTimeOffset(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc));
    }
}