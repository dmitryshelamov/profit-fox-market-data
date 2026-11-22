using Dapper;

using Npgsql;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     Readies the connection of a read of history: opened by the read, so the settings of its session stay for every statement of it
///     (Dapper opens and closes a closed one around each), and given back to the pool reset, so they reach no other request.
/// </summary>
internal static class HistoryConnection
{
    /// <summary>
    ///     Opens the connection and sets its session, waiting for storage <paramref name="timeoutSeconds" /> at most.
    /// </summary>
    /// <exception cref="StorageTimeoutException">Storage did not answer in time.</exception>
    public static async Task PrepareAsync(NpgsqlConnection connection, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(HistoryQueries.ReadSettingsSql, commandTimeout: timeoutSeconds, cancellationToken: cancellationToken));
        }
        catch (NpgsqlException exception) when (exception.InnerException is TimeoutException)
        {
            throw new StorageTimeoutException(timeoutSeconds, exception);
        }
    }
}