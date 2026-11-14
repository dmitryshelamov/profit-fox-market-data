using Npgsql;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

/// <summary>
///     The transaction open in the scope of a request, if any; repositories pass it to every command they run.
/// </summary>
internal sealed class PostgresTransactionAccessor
{
    internal NpgsqlTransaction? Current { get; set; }
}