using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Mediatr;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres;

namespace ProfitFox.MarketData.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>
    ///     MediatR with its handlers and the transaction of a command, the catalog in Postgres with its migrations, the public API of Bybit,
    ///     the background sync of the catalog and the clock.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MediatrRegistrar.Configure(services);
        services.AddSingleton(TimeProvider.System);

        PostgresRegistrar.Configure(configuration, services);

        services.AddBybit();
        services.AddScoped<IExchangeInstruments, BybitExchangeInstruments>();

        // An app with a broken InstrumentSync section stops at start rather than never syncing.
        services.AddOptions<InstrumentSyncSettings>()
            .Bind(configuration.GetSection(InstrumentSyncSettings.Section))
            .Validate(sync => sync.IntervalMinutes > 0, "InstrumentSync:IntervalMinutes must be above zero.")
            .Validate(sync => sync.Categories.Length > 0, "InstrumentSync:Categories must name at least one category.")
            .ValidateOnStart();
        services.AddHostedService<InstrumentSyncBackgroundService>();

        return services;
    }
}