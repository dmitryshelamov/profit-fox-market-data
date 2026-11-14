using System.Net;

using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Tickers;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Integration.Settings;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitTickersClientTests
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
        _tickers = new BybitTickersClient(
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
        _gate.Dispose();
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private StubHttpHandler _bybit = null!;
    private BybitRequestGate _gate = null!;
    private BybitTickersClient _tickers = null!;

    [Test]
    public async Task A_contract_with_an_empty_funding_rate_has_none()
    {
        _bybit.RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT-26DEC26", "810.5", fundingRate: "")));

        IReadOnlyList<BybitLinearTicker> tickers = await _tickers.GetLinearAsync(CancellationToken.None);

        Assert.That(tickers, Is.EqualTo(new[] { new BybitLinearTicker("BTCUSDT-26DEC26", OpenInterest: 810.5m, FundingRate: null) }));
    }

    [Test]
    public async Task A_request_its_caller_gives_up_ends_at_once()
    {
        _bybit.NeverRespond();
        using var givenUp = new CancellationTokenSource();

        Task<IReadOnlyList<BybitLinearTicker>> tickers = _tickers.GetLinearAsync(givenUp.Token);
        await givenUp.CancelAsync();

        // It fails rather than waits for Bybit: the wait here would run out only if it went on.
        Assert.That(() => tickers.WaitAsync(Patience), Throws.Exception.Not.InstanceOf<TimeoutException>());
    }

    [Test]
    public void An_answer_Bybit_refuses_gives_no_tickers()
    {
        _bybit.RespondJson(BybitAnswers.Error(code: 10001, "params error"));

        Assert.ThrowsAsync<HttpRequestException>(() => _tickers.GetLinearAsync(CancellationToken.None));
    }

    [Test]
    public void The_request_goes_through_the_gate_of_Bybit()
    {
        _bybit.RespondJson("", HttpStatusCode.Forbidden);

        Assert.Multiple(() =>
        {
            // The ban pauses the gate, and the pause holds the next request back.
            Assert.ThrowsAsync<ExchangePausedException>(() => _tickers.GetLinearAsync(CancellationToken.None));
            Assert.ThrowsAsync<ExchangePausedException>(() => _tickers.GetLinearAsync(CancellationToken.None));
            Assert.That(_bybit.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task The_tickers_of_the_whole_linear_market_come_in_one_request_exactly_as_Bybit_gave_them()
    {
        _bybit.RespondJson(
            BybitAnswers.Tickers(
                "linear",
                BybitAnswers.LinearTicker("BTCUSDT", "52345.123", "0.00000331"),
                BybitAnswers.LinearTicker("1000PEPEUSDT", "1234567890", "-0.00012")));

        IReadOnlyList<BybitLinearTicker> tickers = await _tickers.GetLinearAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                tickers,
                Is.EqualTo(
                    new[]
                    {
                        new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.00000331m),
                        new BybitLinearTicker("1000PEPEUSDT", OpenInterest: 1234567890m, FundingRate: -0.00012m)
                    }));
            Assert.That(_bybit.Requests.Single().AbsolutePath, Is.EqualTo("/v5/market/tickers"));
            Assert.That(_bybit.Requests.Single().Query, Is.EqualTo("?category=linear"));
        });
    }
}