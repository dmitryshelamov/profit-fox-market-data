using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Tests.Integration.Http;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitInstrumentsClientTests
{
    [SetUp]
    public void SetUp()
    {
        _bybit = new StubHttpHandler();

        // The rate limiter of the SDK is off: it would wait in real time.
        _instruments = new BybitInstrumentsClient(
            new BybitRestClient(
                new HttpClient(_bybit),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })));
    }

    [TearDown]
    public void TearDown()
    {
        _bybit.Dispose();
    }

    private static readonly DateTimeOffset LaunchTime = new DateTimeOffset(year: 2020, month: 3, day: 30, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private StubHttpHandler _bybit = null!;
    private BybitInstrumentsClient _instruments = null!;

    [TestCase("Settling", "LinearPerpetual")]
    [TestCase("Trading", "InversePerpetual")]
    public void A_code_the_catalog_has_no_value_for_fails_the_whole_list(string status, string contractType)
    {
        _bybit.RespondJson(BybitAnswers.List("linear", items: BybitAnswers.LinearContract("BTCUSDT", "BTC", contractType, status)));

        Assert.ThrowsAsync<InvalidOperationException>(() => _instruments.ListAsync(BybitCategory.Linear, CancellationToken.None));
    }

    [TestCase(BybitCategory.Inverse)]
    [TestCase(BybitCategory.Option)]
    public void A_category_the_catalog_does_not_sync_is_refused_without_asking_Bybit(BybitCategory category)
    {
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<NotSupportedException>(() => _instruments.ListAsync(category, CancellationToken.None));
            Assert.That(_bybit.Requests, Is.Empty);
        });
    }

    [Test]
    public void A_page_Bybit_refuses_fails_the_whole_list()
    {
        _bybit
            .RespondJson(BybitAnswers.List("linear", "page2", BybitAnswers.LinearContract("BTCUSDT", "BTC")))
            .RespondJson(BybitAnswers.Error(code: 10001, "params error"));

        Assert.ThrowsAsync<HttpRequestException>(() => _instruments.ListAsync(BybitCategory.Linear, CancellationToken.None));
    }

    [Test]
    public async Task Linear_contracts_are_read_page_by_page_until_Bybit_gives_no_cursor()
    {
        _bybit
            .RespondJson(BybitAnswers.List("linear", "page2", BybitAnswers.LinearContract("BTCUSDT", "BTC")))
            .RespondJson(BybitAnswers.List("linear", items: BybitAnswers.LinearContract("BTCUSDT-26DEC25", "BTC", "LinearFutures")));

        IReadOnlyList<BybitListedInstrument> listed = await _instruments.ListAsync(BybitCategory.Linear, CancellationToken.None);

        var rules = new InstrumentRules(tickSize: 0.1m, quantityStep: 0.001m, minQuantity: 0.001m, minAmount: 5m);
        Assert.Multiple(() =>
        {
            Assert.That(
                listed,
                Is.EqualTo(
                    new[]
                    {
                        new BybitListedInstrument(
                            BybitCategory.Linear,
                            "BTCUSDT",
                            "BTC",
                            "USDT",
                            InstrumentStatus.Trading,
                            rules,
                            BybitContractType.LinearPerpetual,
                            LaunchTime),
                        new BybitListedInstrument(
                            BybitCategory.Linear,
                            "BTCUSDT-26DEC25",
                            "BTC",
                            "USDT",
                            InstrumentStatus.Trading,
                            rules,
                            BybitContractType.LinearFutures,
                            LaunchTime)
                    }));
            Assert.That(_bybit.Requests.Select(request => request.Query), Has.Exactly(1).Contains("cursor=page2"));
            Assert.That(_bybit.Requests, Has.All.Property(nameof(Uri.Query)).Contains("limit=1000"));
        });
    }

    [Test]
    public async Task Spot_pairs_come_with_their_rules_and_no_contract_type()
    {
        _bybit.RespondJson(
            BybitAnswers.List(
                "spot",
                items: BybitAnswers.SpotPair("BTCUSDT", "BTC", "USDT", "0.01", "0.000001", "0.000048", "1")));

        IReadOnlyList<BybitListedInstrument> listed = await _instruments.ListAsync(BybitCategory.Spot, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                listed,
                Is.EqualTo(
                    new[]
                    {
                        new BybitListedInstrument(
                            BybitCategory.Spot,
                            "BTCUSDT",
                            "BTC",
                            "USDT",
                            InstrumentStatus.Trading,
                            new InstrumentRules(tickSize: 0.01m, quantityStep: 0.000001m, minQuantity: 0.000048m, minAmount: 1m))
                    }));
            Assert.That(_bybit.Requests.Single().Query, Does.Contain("category=spot"));
        });
    }
}