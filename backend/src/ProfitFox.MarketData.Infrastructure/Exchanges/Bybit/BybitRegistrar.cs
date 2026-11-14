using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Application.Ports.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

internal static class BybitRegistrar
{
    // The topics the socket client puts on a connection before it opens another, as profit-fox streams the whole market; Bybit takes
    // about eight times as many by the length of their names.
    private const int StreamTopicsPerConnection = 100;

    private static readonly Uri TradeArchiveAddress = new Uri("https://public.bybit.com/");

    /// <summary>
    ///     The public API, the public streams and the public archive of trades of Bybit and one gate for the process, which every client
    ///     of its API sends its requests through; an app with a broken BybitApi section stops at start.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<BybitApiSettings>()
            .Bind(configuration.GetSection(BybitApiSettings.Section))
            .Validate(bybit => bybit.RequestsPerSecond > 0, "BybitApi:RequestsPerSecond must be above zero.")
            .Validate(bybit => bybit.LimitPauseSeconds > 0, "BybitApi:LimitPauseSeconds must be above zero.")
            .Validate(bybit => bybit.BanPauseMinutes > 0, "BybitApi:BanPauseMinutes must be above zero.")
            .ValidateOnStart();

        services.AddBybit(options => options.Socket.SocketIndividualSubscriptionCombineTarget = StreamTopicsPerConnection);
        services.AddSingleton<BybitRequestGate>();
        services.AddScoped<IBybitInstrumentsClient, BybitInstrumentsClient>();
        services.AddScoped<IExchangeCandlesClient, BybitCandlesClient>();
        services.AddSingleton<IBybitCandleStreamClient, BybitCandleStreamClient>();
        services.AddHttpClient<IBybitTradeArchiveClient, BybitTradeArchiveClient>(http => http.BaseAddress = TradeArchiveAddress);
    }
}