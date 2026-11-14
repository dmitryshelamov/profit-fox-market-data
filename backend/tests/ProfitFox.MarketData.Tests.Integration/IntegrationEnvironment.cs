using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

using Testcontainers.PostgreSql;

namespace ProfitFox.MarketData.Tests.Integration;

/// <summary>
///     Starts one TimescaleDB container for the whole test assembly and migrates its database; <see cref="ResetDatabase" /> empties the
///     tables before a test.
/// </summary>
[SetUpFixture]
public class IntegrationEnvironment
{
    // Keep in sync with docker-compose.yml.
    private const string TimescaleImage = "timescale/timescaledb:2.30.2-pg17";

    private static PostgreSqlContainer Container = null!;

    /// <summary>
    ///     The connection string, with its password, of the migrated test database.
    /// </summary>
    public static string ConnectionString => Container.GetConnectionString();

    [OneTimeSetUp]
    public async Task StartContainer()
    {
        // A test database needs no durability: without syncing to disk the tests run faster.
        Container = new PostgreSqlBuilder(TimescaleImage)
            .WithCommand("-c", "fsync=off", "-c", "full_page_writes=off")
            .Build();
        await Container.StartAsync();

        await using ServiceProvider services = Services();
        services.MigrateToLatestVersion();
    }

    [OneTimeTearDown]
    public async Task StopContainer()
    {
        await Container.DisposeAsync();
    }

    /// <summary>
    ///     The services of the infrastructure over the test database.
    /// </summary>
    public static ServiceProvider Services()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = ConnectionString })
            .Build();

        return new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
    }

    /// <summary>
    ///     Empties every table the migrations made.
    /// </summary>
    public static async Task ResetDatabase()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("TRUNCATE instruments, bybit_listings, tinvest_shares, candles, candle_series", connection);
        await command.ExecuteNonQueryAsync();
    }
}