using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

namespace ProfitFox.MarketData.Infrastructure.Observability;

internal static class MetricsRegistrar
{
    // Prometheus turns it into the job label, which tells the service from the services of profit-fox on the same dashboards.
    private const string ServiceName = "profit-fox-market-data";

    /// <summary>
    ///     Pushes the metrics of the calls to the exchanges over OTLP (<c>http/protobuf</c>, cumulative) to the receiver of the settings,
    ///     as profit-fox does; without a receiver nothing listens, so the instruments record into nothing and no connection is opened. An
    ///     app with a broken Metrics section stops at start.
    /// </summary>
    /// <exception cref="InvalidOperationException">The Metrics section is broken.</exception>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        MetricsSettings settings = configuration.GetSection(MetricsSettings.Section).Get<MetricsSettings>() ?? new MetricsSettings();

        if (settings.OtlpEndpoint == "")
        {
            return;
        }

        if (!Uri.TryCreate(settings.OtlpEndpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Metrics:OtlpEndpoint must be an http or https address, or empty.");
        }

        if (settings.ExportInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Metrics:ExportInterval must be above zero.");
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithMetrics(metrics => metrics
                .AddMeter(BybitRestMetricsHandler.MeterName, TInvestMetrics.MeterName)
                .AddOtlpExporter((exporter, reader) =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = (int)settings.ExportInterval.TotalMilliseconds;
                }));
    }
}