using System.Data.Common;
using System.Runtime.CompilerServices;

using Dapper;

using Npgsql;

using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     Reads the rows of one statement of a read of history one by one, as they are asked for. Not Dapper's
///     <c>QueryUnbufferedAsync</c>: it asks Postgres to cancel every statement whose reader it finds open at the end, even one read to
///     its last row, and each such request opens a connection of its own to the server, a couple of milliseconds that a read of years
///     pays once a statement.
/// </summary>
internal static class HistoryRows
{
    /// <summary>
    ///     The rows of <paramref name="sql" />, each made by Dapper from its columns, each answer of storage waited for
    ///     <paramref name="timeoutSeconds" /> at most. A caller that stops early leaves the reader to read the rest of the statement; a
    ///     cancelled <paramref name="cancellationToken" /> cancels the statement.
    /// </summary>
    /// <exception cref="StorageTimeoutException">Storage did not answer in time.</exception>
    public static async IAsyncEnumerable<T> ReadAsync<T>(
        NpgsqlConnection connection,
        string sql,
        object parameters,
        int timeoutSeconds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // A row is given outside the try: C# yields no row from a try with a catch (CS1626).
        DbDataReader reader;

        try
        {
            reader = await connection.ExecuteReaderAsync(new CommandDefinition(sql, parameters, commandTimeout: timeoutSeconds, cancellationToken: cancellationToken));
        }
        catch (NpgsqlException exception) when (exception.InnerException is TimeoutException)
        {
            throw new StorageTimeoutException(timeoutSeconds, exception);
        }

        await using (reader)
        {
            Func<DbDataReader, T> parse = reader.GetRowParser<T>();

            while (await NextAsync(reader, timeoutSeconds, cancellationToken))
            {
                yield return parse(reader);
            }
        }
    }

    private static async Task<bool> NextAsync(DbDataReader reader, int timeoutSeconds, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken);
        }
        catch (NpgsqlException exception) when (exception.InnerException is TimeoutException)
        {
            throw new StorageTimeoutException(timeoutSeconds, exception);
        }
    }
}