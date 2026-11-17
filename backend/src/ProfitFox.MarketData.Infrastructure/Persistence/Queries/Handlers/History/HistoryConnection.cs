using Dapper;

using Npgsql;

using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     Readies the connection of a read of history: opened by the read, so the settings of its session stay for every statement of it
///     (Dapper opens and closes a closed one around each), and given back to the pool reset, so they reach no other request.
/// </summary>
internal static class HistoryConnection
{
    public static async Task PrepareAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(HistoryQueries.ReadSettingsSql, cancellationToken: cancellationToken));
    }
}