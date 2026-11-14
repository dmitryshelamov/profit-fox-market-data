using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Application.Ports.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

internal static class BybitRegistrar
{
    /// <summary>
    ///     The public API of Bybit and one gate for the process, which every client of Bybit sends its requests through; an app with a broken
    ///     BybitApi section stops at start.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<BybitApiSettings>()
            .Bind(configuration.GetSection(BybitApiSettings.Section))
            .Validate(bybit => bybit.RequestsPerSecond > 0, "BybitApi:RequestsPerSecond must be above zero.")
            .Validate(bybit => bybit.LimitPauseSeconds > 0, "BybitApi:LimitPauseSeconds must be above zero.")
            .Validate(bybit => bybit.BanPauseMinutes > 0, "BybitApi:BanPauseMinutes must be above zero.")
            .ValidateOnStart();

        services.AddBybit();
        services.AddSingleton<BybitRequestGate>();
        services.AddScoped<IBybitInstrumentsClient, BybitInstrumentsClient>();
        services.AddScoped<IBybitCandlesClient, BybitCandlesClient>();
    }
}