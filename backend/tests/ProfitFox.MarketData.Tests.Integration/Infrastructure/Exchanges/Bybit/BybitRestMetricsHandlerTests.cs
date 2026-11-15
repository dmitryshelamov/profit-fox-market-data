using System.Diagnostics.Metrics;
using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Tests.Integration.Http;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitRestMetricsHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _requests = new MetricCollector<long>(Meters, BybitRestMetricsHandler.MeterName, "profitfox.bybit.rest.requests");
        _bybit = new StubHttpHandler();
        _http = new HttpClient(new BybitRestMetricsHandler(Meters) { InnerHandler = _bybit });
    }

    [TearDown]
    public void TearDown()
    {
        _http.Dispose();
        _bybit.Dispose();
        _requests.Dispose();
        _services.Dispose();
    }

    private ServiceProvider _services = null!;
    private MetricCollector<long> _requests = null!;
    private StubHttpHandler _bybit = null!;
    private HttpClient _http = null!;

    // The provider owns and disposes the factory.
    private IMeterFactory Meters => _services.GetRequiredService<IMeterFactory>();

    private void AssertCounted(string endpoint, int? status, string outcome)
    {
        var tags = new Dictionary<string, object?> { ["bybit.endpoint"] = endpoint, ["bybit.outcome"] = outcome };

        if (status is not null)
        {
            tags["http.response.status_code"] = status;
        }

        IReadOnlyList<CollectedMeasurement<long>> counted = _requests.GetMeasurementSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(counted.Select(measurement => measurement.Value), Is.EqualTo(new[] { 1L }));
            Assert.That(counted.Single().Tags, Is.EquivalentTo(tags));
        });
    }

    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    public async Task A_refusal_over_the_limit_or_by_the_ip_is_rate_limited(HttpStatusCode status)
    {
        _bybit.RespondJson("", status);

        await _http.GetAsync("https://api.bybit.com/v5/market/tickers?category=linear");

        AssertCounted("/v5/market/tickers", (int)status, "rate_limited");
    }

    // Not the form of Bybit: the request itself must not fail because of the metrics.
    [TestCase("<html>maintenance</html>")]
    [TestCase("""{"result":{}}""")]
    [TestCase("[1]")]
    [TestCase("""{"retCode":"10006"}""")]
    [TestCase("""{"retCode":1.5}""")]
    public async Task An_ok_answer_without_a_whole_number_code_is_ok(string json)
    {
        _bybit.RespondJson(json);

        await _http.GetAsync("https://api.bybit.com/v5/market/time");

        AssertCounted("/v5/market/time", status: 200, "ok");
    }

    [Test]
    public async Task A_failed_answer_is_an_error()
    {
        _bybit.RespondJson("", HttpStatusCode.BadGateway);

        await _http.GetAsync("https://api.bybit.com/v5/market/tickers?category=linear");

        AssertCounted("/v5/market/tickers", status: 502, "error");
    }

    // The archive of trades is not the API and has no limit of it.
    [Test]
    public async Task A_request_to_another_host_is_not_counted()
    {
        _bybit.RespondBytes([1, 2, 3]);

        await _http.GetAsync("https://public.bybit.com/trading/BTCUSDT/BTCUSDT2026-11-14.csv.gz");

        Assert.That(_requests.GetMeasurementSnapshot(), Is.Empty);
    }

    [Test]
    public void An_attempt_without_an_answer_is_an_error_without_a_code_and_fails_as_it_did()
    {
        var failure = new HttpRequestException("connection reset");
        _bybit.Fail(failure);

        HttpRequestException? thrown = Assert.ThrowsAsync<HttpRequestException>(() => _http.GetAsync("https://api.bybit.com/v5/market/tickers?category=linear"));

        Assert.That(thrown, Is.SameAs(failure));
        AssertCounted("/v5/market/tickers", status: null, "error");
    }

    // 10001: a parameter of the request is wrong.
    [Test]
    public async Task Another_code_in_an_ok_answer_is_an_error()
    {
        _bybit.RespondJson("""{"retCode":10001,"retMsg":"params error","result":{},"retExtInfo":{},"time":0}""");

        await _http.GetAsync("https://api.bybit.com/v5/market/open-interest?category=linear&symbol=BTCUSDT&intervalTime=5min");

        AssertCounted("/v5/market/open-interest", status: 200, "error");
    }

    // The limit of the API comes as a code in an OK answer.
    [Test]
    public async Task Code_10006_in_an_ok_answer_is_rate_limited()
    {
        _bybit.RespondJson("""{"retCode":10006,"retMsg":"Too many visits!","result":{},"retExtInfo":{},"time":0}""");

        await _http.GetAsync("https://api.bybit.com/v5/market/kline?category=linear&symbol=BTCUSDT&interval=1");

        AssertCounted("/v5/market/kline", status: 200, "rate_limited");
    }

    // The counter reads the body before the SDK does.
    [Test]
    public async Task Each_request_is_counted_by_its_path_and_its_answer_still_reads_whole()
    {
        const string Answer = """{"retCode":0,"retMsg":"OK","result":{"list":[]},"retExtInfo":{},"time":0}""";
        _bybit.RespondJson(Answer);

        HttpResponseMessage response = await _http.GetAsync("https://api.bybit.com/v5/market/kline?category=linear&symbol=BTCUSDT&interval=1");

        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(Answer));
        AssertCounted("/v5/market/kline", status: 200, "ok");
    }

    // The SDK takes its HTTP client from the factory under a name of its own.
    [Test]
    public void Every_client_of_the_factory_counts_its_requests()
    {
        IServiceCollection services = new ServiceCollection().AddLogging().AddMetrics();
        BybitRegistrar.Configure(new ConfigurationBuilder().Build(), services);
        using ServiceProvider provider = services.BuildServiceProvider();

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler("IBybitRestClient");
        var chain = new List<HttpMessageHandler>();

        while (handler is not null)
        {
            chain.Add(handler);
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        Assert.That(chain, Has.Some.InstanceOf<BybitRestMetricsHandler>());
    }
}