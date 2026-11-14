using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Integration.Http;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Tests.Integration;

/// <summary>
///     The whole app in memory for a test: the Testing environment (appsettings.Testing.json), the database of the test container and
///     Bybit and T-Invest answered by <see cref="Bybit" /> and <see cref="TInvest" />, so no request reaches a real exchange. T-Invest goes
///     with <paramref name="tinvestToken" />; an empty one is an app without a token. The background sync of the catalog, the loading of
///     candles, the stream of closed candles and the filling of buy volumes are left out.
/// </summary>
internal sealed class MarketDataTestApplication(string tinvestToken = "test-token") : WebApplicationFactory<Program>
{
    private const string TestingEnvironment = "Testing";

    /// <summary>
    ///     Answers every request of the app to Bybit.
    /// </summary>
    internal StubHttpHandler Bybit { get; } = new StubHttpHandler();

    /// <summary>
    ///     Answers every call of the app to T-Invest.
    /// </summary>
    internal StubGrpcInvoker TInvest { get; } = new StubGrpcInvoker();

    // The connection string comes from the container: its port is random, so appsettings.Testing.json cannot hold it.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);
        builder.UseSetting("ConnectionStrings:MarketData", IntegrationEnvironment.ConnectionString);
        builder.UseSetting("TInvestApi:Token", tinvestToken);

        // The background sync and loading are left out, so nothing they would do needs a token at start.
        builder.UseSetting("InstrumentSync:TInvest:Enabled", "false");
        builder.UseSetting("CandleSync:TInvest:Enabled", "false");

        builder.ConfigureTestServices(services =>
        {
            // The rate limiter of the SDK is off: it would wait in real time.
            services.AddSingleton<IBybitRestClient>(_ => new BybitRestClient(
                new HttpClient(Bybit, disposeHandler: false),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })));

            services.AddSingleton(new InstrumentsService.InstrumentsServiceClient(TInvest));

            // A test syncs when it asks to: the background services would take the answers it prepared for Bybit.
            services.Remove(services.Single(service => service.ImplementationType == typeof(InstrumentSyncBackgroundService)));
            services.Remove(services.Single(service => service.ImplementationType == typeof(CandleSyncBackgroundService)));

            // The stream would connect to the real Bybit, and the filling of buy volumes would read its archive.
            services.Remove(services.Single(service => service.ImplementationType == typeof(CandleStreamBackgroundService)));
            services.Remove(services.Single(service => service.ImplementationType == typeof(BuyVolumeSyncBackgroundService)));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Bybit.Dispose();
        }
    }
}