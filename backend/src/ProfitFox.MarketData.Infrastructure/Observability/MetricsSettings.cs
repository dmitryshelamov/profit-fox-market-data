namespace ProfitFox.MarketData.Infrastructure.Observability;

/// <summary>
///     The <c>Metrics</c> section of the settings: where and how often the service pushes its metrics.
/// </summary>
internal sealed class MetricsSettings
{
    public const string Section = "Metrics";

    /// <summary>
    ///     The full address of the metrics path of an OTLP receiver, such as <c>http://192.168.8.142:9090/api/v1/otlp/v1/metrics</c>;
    ///     empty pushes nothing.
    /// </summary>
    public string OtlpEndpoint { get; init; } = "";

    public TimeSpan ExportInterval { get; init; } = TimeSpan.FromSeconds(5);
}