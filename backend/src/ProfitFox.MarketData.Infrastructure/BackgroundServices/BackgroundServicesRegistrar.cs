using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

internal static class BackgroundServicesRegistrar
{
    /// <summary>
    ///     The background sync of the catalog, the background loading of candles and the stream of closed candles with their settings; an
    ///     app with a broken section stops at start rather than never working.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
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

        services.AddOptions<CandleStreamSettings>()
            .Bind(configuration.GetSection(CandleStreamSettings.Section))
            .Validate(stream => stream.RefreshSeconds > 0, "CandleStream:RefreshSeconds must be above zero.")
            .Validate(stream => stream.BatchSeconds > 0, "CandleStream:BatchSeconds must be above zero.")
            .ValidateOnStart();
        services.AddHostedService<CandleStreamBackgroundService>();
    }
}