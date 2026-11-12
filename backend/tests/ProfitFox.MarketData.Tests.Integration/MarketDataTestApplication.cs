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
using ProfitFox.MarketData.Tests.Integration.Http;

namespace ProfitFox.MarketData.Tests.Integration;

/// <summary>
///     The whole app in memory for a test: the Testing environment (appsettings.Testing.json), the database of the test container and
///     Bybit answered by <see cref="Bybit" />, so no request reaches the real exchange. The background sync is left out.
/// </summary>
internal sealed class MarketDataTestApplication : WebApplicationFactory<Program>
{
    private const string TestingEnvironment = "Testing";

    /// <summary>
    ///     Answers every request of the app to Bybit.
    /// </summary>
    internal StubHttpHandler Bybit { get; } = new StubHttpHandler();

    // The connection string comes from the container: its port is random, so appsettings.Testing.json cannot hold it.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestingEnvironment);
        builder.UseSetting("ConnectionStrings:MarketData", IntegrationEnvironment.ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            // The rate limiter of the SDK is off: it would wait in real time.
            services.AddSingleton<IBybitRestClient>(_ => new BybitRestClient(
                new HttpClient(Bybit, disposeHandler: false),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })));

            // A test syncs when it asks to: the background sync would take the answers it prepared for Bybit.
            services.Remove(services.Single(service => service.ImplementationType == typeof(InstrumentSyncBackgroundService)));
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