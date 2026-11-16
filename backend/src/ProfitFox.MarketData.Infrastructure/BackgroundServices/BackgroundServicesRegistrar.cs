using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

internal static class BackgroundServicesRegistrar
{
    /// <summary>
    ///     The background sync of the catalog, the background loading of candles, the stream of closed candles, the filling of buy
    ///     volumes, the collection of tickers, the loading of funding payments, the loading of open interest, the stream of liquidations and the
    ///     loading of the long/short ratio with their settings, and the instruments whose whole history they load;
    ///     an app with a broken section stops at start rather than never working.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<FullHistorySettings>().Bind(configuration.GetSection(FullHistorySettings.Section));

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
            .Validate(sync => sync.Parallelism > 0, "CandleSync:Parallelism must be above zero.")
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
            .Validate<IOptionsMonitor<TInvestApiSettings>>(
                (stream, tinvest) => !stream.TInvest.Enabled || tinvest.CurrentValue.HasToken,
                "CandleStream:TInvest needs TInvestApi:Token while it is enabled.")
            .ValidateOnStart();
        services.AddSingleton<ILiveCandleFeed, LiveCandleFeed>();
        services.AddHostedService<CandleStreamBackgroundService>();

        services.AddOptions<BuyVolumeSyncSettings>()
            .Bind(configuration.GetSection(BuyVolumeSyncSettings.Section))
            .Validate(sync => sync.HistoryDays > 0, "BuyVolumeSync:HistoryDays must be above zero.")
            .Validate(sync => sync.IdleSeconds > 0, "BuyVolumeSync:IdleSeconds must be above zero.")
            .Validate(sync => sync.Parallelism > 0, "BuyVolumeSync:Parallelism must be above zero.")
            .ValidateOnStart();
        services.AddHostedService<BuyVolumeSyncBackgroundService>();

        services.AddOptions<TickerCollectionSettings>()
            .Bind(configuration.GetSection(TickerCollectionSettings.Section))
            .Validate(
                collection => collection.DeadlineSeconds is > 0 and < 60,
                "TickerCollection:DeadlineSeconds must be above zero and below 60.")
            .ValidateOnStart();
        services.AddHostedService<TickerCollectionBackgroundService>();

        // One feed a kind of market data: its saving publishes and its gRPC service subscribes. Each is registered by its kind, so a
        // feed of a kind nobody publishes cannot be asked for by mistake.
        services.AddSingleton<ILiveFeed<InstrumentOpenInterestInternal>, LiveFeed<InstrumentOpenInterestInternal>>();
        services.AddSingleton<ILiveFeed<InstrumentFundingRateInternal>, LiveFeed<InstrumentFundingRateInternal>>();
        services.AddSingleton<ILiveFeed<InstrumentFundingPaymentInternal>, LiveFeed<InstrumentFundingPaymentInternal>>();
        services.AddSingleton<ILiveFeed<InstrumentLiquidationMinuteInternal>, LiveFeed<InstrumentLiquidationMinuteInternal>>();
        services.AddSingleton<ILiveFeed<InstrumentLongShortRatioInternal>, LiveFeed<InstrumentLongShortRatioInternal>>();

        services.AddOptions<FundingPaymentSyncSettings>()
            .Bind(configuration.GetSection(FundingPaymentSyncSettings.Section))
            .Validate(sync => sync.HistoryDays > 0, "FundingPaymentSync:HistoryDays must be above zero.")
            .Validate(sync => sync.Parallelism > 0, "FundingPaymentSync:Parallelism must be above zero.")
            .ValidateOnStart();
        services.AddHostedService<FundingPaymentSyncBackgroundService>();

        services.AddOptions<OpenInterestSyncSettings>()
            .Bind(configuration.GetSection(OpenInterestSyncSettings.Section))
            .Validate(sync => sync.HistoryDays > 0, "OpenInterestSync:HistoryDays must be above zero.")
            .Validate(sync => sync.IdleSeconds > 0, "OpenInterestSync:IdleSeconds must be above zero.")
            .Validate(sync => sync.Parallelism > 0, "OpenInterestSync:Parallelism must be above zero.")
            .ValidateOnStart();
        services.AddHostedService<OpenInterestSyncBackgroundService>();

        services.AddOptions<LiquidationStreamSettings>()
            .Bind(configuration.GetSection(LiquidationStreamSettings.Section))
            .Validate(stream => stream.RefreshSeconds > 0, "LiquidationStream:RefreshSeconds must be above zero.")
            .Validate(stream => stream.SaveSeconds > 0, "LiquidationStream:SaveSeconds must be above zero.")
            .ValidateOnStart();
        services.AddHostedService<LiquidationStreamBackgroundService>();

        services.AddOptions<LongShortRatioSyncSettings>()
            .Bind(configuration.GetSection(LongShortRatioSyncSettings.Section))
            .Validate(sync => sync.HistoryDays > 0, "LongShortRatioSync:HistoryDays must be above zero.")
            .Validate(sync => sync.IdleSeconds > 0, "LongShortRatioSync:IdleSeconds must be above zero.")
            .Validate(sync => sync.Parallelism > 0, "LongShortRatioSync:Parallelism must be above zero.")
            .ValidateOnStart();
        services.AddHostedService<LongShortRatioSyncBackgroundService>();
    }
}