using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Integration.Settings;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitInstrumentsClientTests
{
    [SetUp]
    public void SetUp()
    {
        _bybit = new StubHttpHandler();

        // The rate limiter of the SDK is off: it would wait in real time.
        _gate = new BybitRequestGate(
            new FixedOptionsMonitor<BybitApiSettings>(new BybitApiSettings()),
            new ExchangeRetryExecutor(TimeProvider.System),
            TimeProvider.System);
        _instruments = new BybitInstrumentsClient(
            new BybitRestClient(
                new HttpClient(_bybit),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            _gate);
    }

    [TearDown]
    public void TearDown()
    {
        _bybit.Dispose();
    }

    private static readonly DateTimeOffset LaunchTime = new DateTimeOffset(year: 2020, month: 3, day: 30, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private StubHttpHandler _bybit = null!;
    private BybitRequestGate _gate = null!;
    private BybitInstrumentsClient _instruments = null!;

    [TestCase("Settling", "LinearPerpetual")]
    [TestCase("Trading", "InversePerpetual")]
    public void A_code_the_catalog_has_no_value_for_fails_the_whole_list(string status, string contractType)
    {
        _bybit.RespondJson(BybitAnswers.List("linear", items: BybitAnswers.LinearContract("BTCUSDT", "BTC", contractType, status)));

        Assert.ThrowsAsync<InvalidOperationException>(() => _instruments.ListAsync(BybitCategory.Linear, CancellationToken.None));
    }

    [TestCase("innovation", BybitSymbolType.Innovation)]
    [TestCase("adventure", BybitSymbolType.Adventure)]
    [TestCase("xstocks", BybitSymbolType.XStocks)]
    [TestCase("stock", BybitSymbolType.Stock)]
    [TestCase("ETF", BybitSymbolType.Etf)]
    [TestCase("commodity", BybitSymbolType.Commodity)]
    [TestCase("forex", BybitSymbolType.Forex)]
    public async Task A_linear_contract_keeps_the_symbol_type_Bybit_gives(string symbolType, BybitSymbolType expected)
    {
        _bybit.RespondJson(BybitAnswers.List("linear", items: BybitAnswers.LinearContract("TSLLUSDT", "TSLL", symbolType: symbolType)));

        IReadOnlyList<BybitListedInstrument> listed = await _instruments.ListAsync(BybitCategory.Linear, CancellationToken.None);

        Assert.That(listed.Single().SymbolType, Is.EqualTo(expected));
    }

    [TestCase("innovation", BybitSymbolType.Innovation)]
    [TestCase("adventure", BybitSymbolType.Adventure)]
    [TestCase("xstocks", BybitSymbolType.XStocks)]
    [TestCase("stock", BybitSymbolType.Stock)]
    [TestCase("ETF", BybitSymbolType.Etf)]
    [TestCase("commodity", BybitSymbolType.Commodity)]
    [TestCase("forex", BybitSymbolType.Forex)]
    public async Task A_spot_pair_keeps_the_symbol_type_Bybit_gives(string symbolType, BybitSymbolType expected)
    {
        _bybit.RespondJson(BybitAnswers.List("spot", items: BybitAnswers.SpotPair("AAPLXUSDT", "AAPLX", "USDT", "0.01", "0.0001", "0.0001", "1", symbolType)));

        IReadOnlyList<BybitListedInstrument> listed = await _instruments.ListAsync(BybitCategory.Spot, CancellationToken.None);

        Assert.That(listed.Single().SymbolType, Is.EqualTo(expected));
    }

    [Test]
    public async Task A_contract_with_a_symbol_type_the_catalog_does_not_know_has_none_and_the_list_stays_whole()
    {
        _bybit.RespondJson(
            BybitAnswers.List(
                "linear",
                items: [BybitAnswers.LinearContract("USTBUSDT", "USTB", symbolType: "bond"), BybitAnswers.LinearContract("TSLLUSDT", "TSLL", symbolType: "stock")]));

        IReadOnlyList<BybitListedInstrument> listed = await _instruments.ListAsync(BybitCategory.Linear, CancellationToken.None);

        Assert.That(
            listed.Select(instrument => (instrument.Symbol, instrument.SymbolType)),
            Is.EqualTo(new (string, BybitSymbolType?)[] { ("USTBUSDT", null), ("TSLLUSDT", BybitSymbolType.Stock) }));
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
                            BybitInstrumentStatus.Trading,
                            rules,
                            BybitContractType.LinearPerpetual,
                            LaunchTime),
                        new BybitListedInstrument(
                            BybitCategory.Linear,
                            "BTCUSDT-26DEC25",
                            "BTC",
                            "USDT",
                            BybitInstrumentStatus.Trading,
                            rules,
                            BybitContractType.LinearFutures,
                            LaunchTime)
                    }));
            Assert.That(_bybit.Requests.Select(request => request.Query), Has.Exactly(1).Contains("cursor=page2"));
            Assert.That(_bybit.Requests, Has.All.Property(nameof(Uri.Query)).Contains("limit=1000"));
        });
    }

    [Test]
    public async Task Linear_contracts_on_shares_and_funds_are_told_apart_from_crypto()
    {
        _bybit.RespondJson(
            BybitAnswers.List(
                "linear",
                items:
                [
                    BybitAnswers.LinearContract("TSLLUSDT", "TSLL", symbolType: "stock"),
                    BybitAnswers.LinearContract("SPYUSDT", "SPY", symbolType: "ETF"),
                    BybitAnswers.LinearContract("BTCUSDT", "BTC", symbolType: "")
                ]));

        IReadOnlyList<BybitListedInstrument> listed = await _instruments.ListAsync(BybitCategory.Linear, CancellationToken.None);

        Assert.That(
            listed.Select(instrument => (instrument.Symbol, instrument.SymbolType)),
            Is.EqualTo(new (string, BybitSymbolType?)[] { ("TSLLUSDT", BybitSymbolType.Stock), ("SPYUSDT", BybitSymbolType.Etf), ("BTCUSDT", null) }));
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
                            BybitInstrumentStatus.Trading,
                            new InstrumentRules(tickSize: 0.01m, quantityStep: 0.000001m, minQuantity: 0.000048m, minAmount: 1m))
                    }));
            Assert.That(_bybit.Requests.Single().Query, Does.Contain("category=spot"));
        });
    }
}