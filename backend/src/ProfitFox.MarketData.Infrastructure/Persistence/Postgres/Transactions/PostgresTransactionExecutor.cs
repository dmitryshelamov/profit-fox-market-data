using System.Data;

using Npgsql;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

/// <summary>
///     Runs an action in a transaction on the connection of the scope: commits when it ends, rolls back when it throws. An action inside a
///     running transaction joins it.
/// </summary>
internal sealed class PostgresTransactionExecutor(NpgsqlConnection connection, PostgresTransactionAccessor transactions)
{
    internal Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async () =>
            {
                await action();

                return true;
            },
            cancellationToken);
    }

    internal async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        if (transactions.Current is not null)
        {
            return await action();
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
            transactions.Current = transaction;

            try
            {
                TResult result = await action();
                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
            finally
            {
                transactions.Current = null;
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}