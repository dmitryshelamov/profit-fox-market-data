using System.Data.Common;
using System.Runtime.CompilerServices;

using Dapper;

using Npgsql;

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
    ///     The rows of <paramref name="sql" />, each made by Dapper from its columns. A caller that stops early leaves the reader to read
    ///     the rest of the statement; a cancelled <paramref name="cancellationToken" /> cancels the statement.
    /// </summary>
    public static async IAsyncEnumerable<T> ReadAsync<T>(
        NpgsqlConnection connection,
        string sql,
        object parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using DbDataReader reader = await connection.ExecuteReaderAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        Func<DbDataReader, T> parse = reader.GetRowParser<T>();

        while (await reader.ReadAsync(cancellationToken))
        {
            yield return parse(reader);
        }
    }
}