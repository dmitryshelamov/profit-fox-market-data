using Dapper;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Converters;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments;

/// <summary>
///     Works on the connection of the scope, in the transaction of the command if one runs; Dapper opens a closed connection itself.
/// </summary>
internal sealed class InstrumentRepository(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    PostgresTransactionExecutor transactionExecutor) : IInstrumentRepository
{
    public async Task<IReadOnlyList<Instrument>> GetAllAsync(CancellationToken cancellationToken)
    {
        IEnumerable<InstrumentDb> rows = await connection.QueryAsync<InstrumentDb>(InstrumentRepositoryQueries.All(transactions.Current, cancellationToken));

        return [.. rows.Select(InstrumentDbConverter.ToDomain)];
    }

    // Saves all together even outside a command: without a transaction of its own it opens one.
    public async Task SaveAsync(IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken)
    {
        if (instruments.Count == 0)
        {
            return;
        }

        List<InstrumentDb> rows = [.. instruments.Select(InstrumentDbConverter.FromDomain)];

        await transactionExecutor.ExecuteAsync(
            () => connection.ExecuteAsync(InstrumentRepositoryQueries.Upsert(rows, transactions.Current, cancellationToken)),
            cancellationToken);
    }
}