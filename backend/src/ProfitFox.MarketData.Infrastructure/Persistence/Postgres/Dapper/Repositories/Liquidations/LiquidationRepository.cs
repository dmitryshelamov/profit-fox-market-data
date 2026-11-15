using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations;

/// <summary>
///     Works on the connection of the scope; Dapper opens a closed connection itself. The minutes are rows of the hypertable
///     <c>liquidations</c>, and each call is one statement, so it is whole without a transaction; a statement that changes rows gives
///     them back as it left them.
/// </summary>
internal sealed class LiquidationRepository(NpgsqlConnection connection) : ILiquidationRepository
{
    public async Task<IReadOnlyList<StoredLiquidationMinute>> AddAsync(IReadOnlyCollection<LiquidationMinute> minutes, CancellationToken cancellationToken)
    {
        return [.. (await connection.QueryAsync<StoredLiquidationMinuteDb>(LiquidationRepositoryQueries.Add(minutes, cancellationToken))).Select(ToDomain)];
    }

    public async Task<IReadOnlyList<StoredLiquidationMinute>> InvalidateAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        IEnumerable<StoredLiquidationMinuteDb> rows = await connection.QueryAsync<StoredLiquidationMinuteDb>(
            LiquidationRepositoryQueries.Invalidate(instrumentIds, from, until, cancellationToken));

        return [.. rows.Select(ToDomain)];
    }

    public async Task<DateTimeOffset?> GetLastTimeAsync(CancellationToken cancellationToken)
    {
        DateTime? last = await connection.ExecuteScalarAsync<DateTime?>(LiquidationRepositoryQueries.LastTime(cancellationToken));

        return last is null ? null : new DateTimeOffset(DateTime.SpecifyKind(last.Value, DateTimeKind.Utc));
    }

    private static StoredLiquidationMinute ToDomain(StoredLiquidationMinuteDb row)
    {
        return new StoredLiquidationMinute(
            new LiquidationMinute(
                row.InstrumentId,
                row.Time,
                new LiquidationSum(row.LongVolume, row.LongTurnover, row.LongCount),
                new LiquidationSum(row.ShortVolume, row.ShortTurnover, row.ShortCount)),
            row.IsValid);
    }
}