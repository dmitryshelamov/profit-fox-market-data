using System.Diagnostics.Metrics;
using System.Globalization;

using Grpc.Core;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The metrics of the calls to T-Invest on the meter <c>ProfitFox.TInvest</c>, with the names and tags of profit-fox: every unary
///     call counts once as <c>profitfox.tinvest.requests</c> by group, method and outcome (<c>ok</c>, <c>rate_limited</c>,
///     <c>error</c>); the headers <c>x-ratelimit-limit</c> and <c>x-ratelimit-remaining</c> of a group give the gauges
///     <c>profitfox.tinvest.limit</c> (the last limit) and <c>profitfox.tinvest.limit_remaining</c> (the lowest remainder since the
///     previous collection, which takes it). One for the process.
/// </summary>
internal sealed class TInvestMetrics
{
    public const string MeterName = "ProfitFox.TInvest";

    private readonly Counter<long> _requests;
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, int> _limits = [];
    private readonly Dictionary<string, int> _lowest = [];

    public TInvestMetrics(IMeterFactory meters)
    {
        Meter meter = meters.Create(MeterName);
        _requests = meter.CreateCounter<long>("profitfox.tinvest.requests", "{request}");
        meter.CreateObservableGauge("profitfox.tinvest.limit", Limits, "{request}");
        meter.CreateObservableGauge("profitfox.tinvest.limit_remaining", TakeRemaining, "{request}");
    }

    /// <summary>
    ///     The group of the limits of T-Invest a service of the calls belongs to, as profit-fox labels it: <c>instruments</c> or
    ///     <c>market_data</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a service of the Invest API the service calls.</exception>
    public static string GroupOf(string serviceName)
    {
        string service = serviceName[(serviceName.LastIndexOf('.') + 1)..];

        return service switch
        {
            "InstrumentsService" => "instruments",
            "MarketDataService" => "market_data",
            _ => throw new ArgumentOutOfRangeException(nameof(serviceName), serviceName, "Not a service of the Invest API the service calls.")
        };
    }

    public void Count(string group, string method, string outcome)
    {
        _requests.Add(
            1,
            new KeyValuePair<string, object?>("group", group),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>
    ///     Reads the limit headers of an answer, if it carries both.
    /// </summary>
    public void Observe(string group, Metadata headers)
    {
        if (Header(headers, "x-ratelimit-limit") is not { } limit || Header(headers, "x-ratelimit-remaining") is not { } remaining)
        {
            return;
        }

        lock (_lock)
        {
            _limits[group] = limit;
            _lowest[group] = _lowest.TryGetValue(group, out int lowest) ? Math.Min(lowest, remaining) : remaining;
        }
    }

    private IEnumerable<Measurement<int>> Limits()
    {
        lock (_lock)
        {
            return Measurements(_limits);
        }
    }

    private IEnumerable<Measurement<int>> TakeRemaining()
    {
        lock (_lock)
        {
            Measurement<int>[] lowest = Measurements(_lowest);
            _lowest.Clear();

            return lowest;
        }
    }

    private static int? Header(Metadata headers, string name)
    {
        return headers.GetValue(name) is { } value && int.TryParse(value, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }

    private static Measurement<int>[] Measurements(Dictionary<string, int> values)
    {
        return [.. values.Select(entry => new Measurement<int>(entry.Value, new KeyValuePair<string, object?>("group", entry.Key)))];
    }
}