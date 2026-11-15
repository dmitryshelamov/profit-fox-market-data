using System.Diagnostics.Metrics;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Tests.Integration.Grpc;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.TInvest;

[TestFixture]
public class TInvestMetricsInterceptorTests
{
    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _requests = new MetricCollector<long>(Meters, TInvestMetrics.MeterName, "profitfox.tinvest.requests");
        _api = new StubGrpcInvoker();
        CallInvoker invoker = _api.Intercept(new TInvestMetricsInterceptor(new TInvestMetrics(Meters)));
        _instruments = new InstrumentsService.InstrumentsServiceClient(invoker);
        _marketData = new MarketDataService.MarketDataServiceClient(invoker);
    }

    [TearDown]
    public void TearDown()
    {
        _requests.Dispose();
        _services.Dispose();
    }

    private ServiceProvider _services = null!;
    private MetricCollector<long> _requests = null!;
    private StubGrpcInvoker _api = null!;
    private InstrumentsService.InstrumentsServiceClient _instruments = null!;
    private MarketDataService.MarketDataServiceClient _marketData = null!;

    // The provider owns and disposes the factory.
    private IMeterFactory Meters => _services.GetRequiredService<IMeterFactory>();

    private static Metadata Limits(string limit, string remaining)
    {
        return new Metadata { { "x-ratelimit-limit", limit }, { "x-ratelimit-remaining", remaining } };
    }

    private void AssertCounted(string group, string method, string outcome)
    {
        IReadOnlyList<CollectedMeasurement<long>> counted = _requests.GetMeasurementSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(counted.Select(measurement => measurement.Value), Is.EqualTo(new[] { 1L }));
            Assert.That(
                counted.Single().Tags,
                Is.EquivalentTo(new Dictionary<string, object?> { ["group"] = group, ["method"] = method, ["outcome"] = outcome }));
        });
    }

    [TestCase(StatusCode.ResourceExhausted, "rate_limited")]
    [TestCase(StatusCode.Unavailable, "error")]
    [TestCase(StatusCode.DeadlineExceeded, "error")]
    public void A_failed_call_is_counted_by_its_status_and_fails_as_it_did(StatusCode status, string outcome)
    {
        _api.Fail("GetCandles", status);

        RpcException? failure = Assert.ThrowsAsync<RpcException>(async () => await _marketData.GetCandlesAsync(new GetCandlesRequest()));

        Assert.That(failure!.StatusCode, Is.EqualTo(status));
        AssertCounted("market_data", "GetCandles", outcome);
    }

    [Test]
    public async Task A_call_of_candles_is_counted_in_the_group_of_market_data()
    {
        _api.Respond("GetCandles", TInvestAnswers.Candles());

        await _marketData.GetCandlesAsync(new GetCandlesRequest());

        AssertCounted("market_data", "GetCandles", "ok");
    }

    [Test]
    public async Task A_call_of_the_shares_is_counted_in_the_group_of_instruments_and_answers_as_it_did()
    {
        _api.Respond("Shares", TInvestAnswers.Shares(TInvestAnswers.Share("SBER", "e6123145-9665-43e0-8413-cd61b8aa9b13")));

        SharesResponse response = await _instruments.SharesAsync(new InstrumentsRequest());

        Assert.That(response.Instruments.Select(share => share.Ticker), Is.EqualTo(new[] { "SBER" }));
        AssertCounted("instruments", "Shares", "ok");
    }

    [Test]
    public async Task A_collection_takes_the_lowest_remainder_and_the_next_one_tells_only_what_came_after_it()
    {
        using var remaining = new MetricCollector<int>(Meters, TInvestMetrics.MeterName, "profitfox.tinvest.limit_remaining");
        _api.Respond("GetCandles", TInvestAnswers.Candles(), Limits("100", "40"));
        await _marketData.GetCandlesAsync(new GetCandlesRequest());
        remaining.RecordObservableInstruments();

        remaining.RecordObservableInstruments();
        int afterNothing = remaining.GetMeasurementSnapshot().Count;
        _api.Respond("GetCandles", TInvestAnswers.Candles(), Limits("100", "70"));
        await _marketData.GetCandlesAsync(new GetCandlesRequest());
        remaining.RecordObservableInstruments();

        Assert.Multiple(() =>
        {
            Assert.That(afterNothing, Is.EqualTo(1));
            Assert.That(remaining.LastMeasurement!.Value, Is.EqualTo(70));
        });
    }

    [Test]
    public async Task The_limit_headers_of_the_answers_reach_the_gauges_by_group()
    {
        using var limits = new MetricCollector<int>(Meters, TInvestMetrics.MeterName, "profitfox.tinvest.limit");
        using var remaining = new MetricCollector<int>(Meters, TInvestMetrics.MeterName, "profitfox.tinvest.limit_remaining");

        foreach (string left in new[] { "90", "40", "70" })
        {
            _api.Respond("GetCandles", TInvestAnswers.Candles(), Limits("100", left));
            await _marketData.GetCandlesAsync(new GetCandlesRequest());
        }

        limits.RecordObservableInstruments();
        remaining.RecordObservableInstruments();

        var group = new Dictionary<string, object?> { ["group"] = "market_data" };
        Assert.Multiple(() =>
        {
            Assert.That(limits.LastMeasurement!.Value, Is.EqualTo(100));
            Assert.That(limits.LastMeasurement.Tags, Is.EquivalentTo(group));
            Assert.That(remaining.LastMeasurement!.Value, Is.EqualTo(40));
            Assert.That(remaining.LastMeasurement.Tags, Is.EquivalentTo(group));
        });
    }
}