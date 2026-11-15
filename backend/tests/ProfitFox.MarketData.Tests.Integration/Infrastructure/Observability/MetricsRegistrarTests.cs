using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Metrics;

using ProfitFox.MarketData.Infrastructure.Observability;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Observability;

[TestFixture]
public class MetricsRegistrarTests
{
    private static ServiceProvider Services(Dictionary<string, string?> metrics)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(metrics).Build();
        IServiceCollection services = new ServiceCollection().AddLogging().AddMetrics();
        MetricsRegistrar.Configure(configuration, services);

        return services.BuildServiceProvider();
    }

    [TestCase("192.168.8.142:9090")]
    [TestCase("ftp://192.168.8.142/metrics")]
    public void A_receiver_that_is_not_an_http_address_stops_the_app(string endpoint)
    {
        Assert.Throws<InvalidOperationException>(() => Services(new Dictionary<string, string?> { ["Metrics:OtlpEndpoint"] = endpoint }));
    }

    [TestCase("00:00:00")]
    [TestCase("-00:00:05")]
    public void An_export_interval_of_no_time_stops_the_app(string interval)
    {
        Assert.Throws<InvalidOperationException>(() => Services(
            new Dictionary<string, string?>
            {
                ["Metrics:OtlpEndpoint"] = "http://192.168.8.142:9090/api/v1/otlp/v1/metrics",
                ["Metrics:ExportInterval"] = interval
            }));
    }

    [Test]
    public void A_receiver_in_the_settings_gets_the_metrics()
    {
        using ServiceProvider services = Services(new Dictionary<string, string?> { ["Metrics:OtlpEndpoint"] = "http://192.168.8.142:9090/api/v1/otlp/v1/metrics" });

        Assert.That(services.GetService<MeterProvider>(), Is.Not.Null);
    }

    [Test]
    public void No_receiver_pushes_nothing()
    {
        using ServiceProvider services = Services([]);

        Assert.That(services.GetService<MeterProvider>(), Is.Null);
    }
}