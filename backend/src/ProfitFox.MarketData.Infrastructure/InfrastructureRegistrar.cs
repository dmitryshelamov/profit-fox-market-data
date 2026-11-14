using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Ports.Candles;
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
    ///     MediatR with its handlers and the transaction of a command, the catalog in Postgres with its migrations, the public API of Bybit,
    ///     the Invest API of T-Invest, the background sync of the catalog, the background loading of candles and the clock.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        MediatrRegistrar.Configure(services);
        services.AddSingleton(TimeProvider.System);

        PostgresRegistrar.Configure(configuration, services);

        services.AddSingleton<ExchangeRetryExecutor>();
        BybitRegistrar.Configure(configuration, services);

        TInvestRegistrar.Configure(configuration, services);

        // An app with a broken InstrumentSync section stops at start rather than never syncing.
        services.AddOptions<InstrumentSyncSettings>()
            .Bind(configuration.GetSection(InstrumentSyncSettings.Section))
            .Validate(sync => sync.IntervalMinutes > 0, "InstrumentSync:IntervalMinutes must be above zero.")
            .Validate(
                sync => sync.Bybit.Enabled || sync.TInvest.Enabled,
                "InstrumentSync must sync something: enable InstrumentSync:Bybit or InstrumentSync:TInvest.")
            .Validate(
                sync => !sync.Bybit.Enabled || sync.Bybit.Categories.Length > 0,
                "InstrumentSync:Bybit:Categories must name at least one category while Bybit is enabled.")
            .Validate<IOptionsMonitor<TInvestApiSettings>>(
                (sync, tinvest) => !sync.TInvest.Enabled || tinvest.CurrentValue.HasToken,
                "InstrumentSync:TInvest needs TInvestApi:Token while it is enabled.")
            .ValidateOnStart();
        services.AddHostedService<InstrumentSyncBackgroundService>();

        // An app with a broken CandleSync section stops at start rather than never loading.
        services.AddOptions<CandleSyncSettings>()
            .Bind(configuration.GetSection(CandleSyncSettings.Section))
            .Validate(sync => sync.HistoryDays > 0, "CandleSync:HistoryDays must be above zero.")
            .Validate(sync => sync.IdleSeconds > 0, "CandleSync:IdleSeconds must be above zero.")
            .Validate(
                sync => sync.Bybit.Enabled || sync.TInvest.Enabled,
                "CandleSync must load something: enable CandleSync:Bybit or CandleSync:TInvest.")
            .Validate<IOptionsMonitor<TInvestApiSettings>>(
                (sync, tinvest) => !sync.TInvest.Enabled || tinvest.CurrentValue.HasToken,
                "CandleSync:TInvest needs TInvestApi:Token while it is enabled.")
            .ValidateOnStart();
        services.AddSingleton<CandleLoadQueue>();
        services.AddSingleton<ICandleLoadQueue>(provider => provider.GetRequiredService<CandleLoadQueue>());
        services.AddHostedService<CandleSyncBackgroundService>();

        return services;
    }
}