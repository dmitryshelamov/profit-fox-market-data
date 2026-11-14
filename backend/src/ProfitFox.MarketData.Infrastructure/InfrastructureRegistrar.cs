using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Infrastructure.Mediatr;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres;

namespace ProfitFox.MarketData.Infrastructure;

public static class InfrastructureRegistrar
{
    /// <summary>
    ///     MediatR with its handlers, the catalog and the candles in Postgres with their migrations, the public API of Bybit, the Invest
    ///     API of T-Invest, the background sync of the catalog, the background loading of candles and the clock.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MediatrRegistrar.Configure(services);
        services.AddSingleton(TimeProvider.System);

        PostgresRegistrar.Configure(configuration, services);

        services.AddSingleton<ExchangeRetryExecutor>();
        BybitRegistrar.Configure(configuration, services);

        TInvestRegistrar.Configure(configuration, services);

        BackgroundServicesRegistrar.Configure(configuration, services);

        return services;
    }
}