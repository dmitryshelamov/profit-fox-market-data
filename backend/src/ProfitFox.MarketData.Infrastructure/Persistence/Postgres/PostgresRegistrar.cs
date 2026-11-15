using FluentMigrator.Runner;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.LongShortRatio;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres;

internal static class PostgresRegistrar
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     The connection string is read on first use, so a test host can still replace it after registration. A scope (a request) has one
    ///     connection, so its repositories share the transaction of its command. The health checks ask the database.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(ConnectionString(configuration)));
        services.AddScoped(provider => provider.GetRequiredService<NpgsqlDataSource>().CreateConnection());
        services.AddScoped<PostgresTransactionAccessor>();
        services.AddScoped<PostgresTransactionExecutor>();
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<ICandleSeriesRepository, CandleSeriesRepository>();
        services.AddScoped<IOpenInterestRepository, OpenInterestRepository>();
        services.AddScoped<IOpenInterestSeriesRepository, OpenInterestSeriesRepository>();
        services.AddScoped<IFundingRateRepository, FundingRateRepository>();
        services.AddScoped<IFundingPaymentSeriesRepository, FundingPaymentSeriesRepository>();
        services.AddScoped<ILiquidationRepository, LiquidationRepository>();
        services.AddScoped<ILongShortRatioSeriesRepository, LongShortRatioSeriesRepository>();

        // A database that does not answer in time is as unhealthy as one that fails.
        services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", timeout: HealthCheckTimeout);

        services
            .AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(_ => ConnectionString(configuration))
                .ScanIn(typeof(PostgresRegistrar).Assembly)
                .For.Migrations());
    }

    private static string ConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("MarketData")
            ?? throw new InvalidOperationException("ConnectionStrings:MarketData is not set.");
    }
}