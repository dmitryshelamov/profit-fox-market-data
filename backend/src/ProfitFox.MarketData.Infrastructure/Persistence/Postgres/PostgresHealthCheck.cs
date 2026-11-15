using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres;

/// <summary>
///     The database answers a query. A failure or a timeout of the query makes the service unhealthy: the health checks tell it by its
///     exception.
/// </summary>
internal sealed class PostgresHealthCheck(NpgsqlDataSource database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = database.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken);

        return HealthCheckResult.Healthy();
    }
}