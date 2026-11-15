using System.Net;

using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.OpenInterest;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitOpenInterestClientTests
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
        _openInterest = new BybitOpenInterestClient(
            new BybitRestClient(
                new HttpClient(_bybit),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            _gate,
            NullLogger<BybitOpenInterestClient>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _bybit.Dispose();
        _gate.Dispose();
    }

    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly BybitListedInstrument Btc = BybitListedInstrumentGenerator.GenerateLinear();

    private StubHttpHandler _bybit = null!;
    private BybitRequestGate _gate = null!;
    private BybitOpenInterestClient _openInterest = null!;

    [Test]
    public async Task A_contract_asked_for_the_time_before_its_launch_has_no_points()
    {
        _bybit.RespondJson(BybitAnswers.OpenInterestHistory("BTCUSDT"));

        IReadOnlyList<BybitOpenInterestPoint> points = await _openInterest.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None);

        Assert.That(points, Is.Empty);
    }

    [Test]
    public async Task A_page_holds_the_two_hundred_points_Bybit_gives_at_most()
    {
        _bybit.RespondJson(BybitAnswers.OpenInterestHistory("BTCUSDT"));

        await _openInterest.GetAsync(Btc, Noon - _openInterest.PageLength, Noon, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_openInterest.PageLength, Is.EqualTo(TimeSpan.FromMinutes(1000)));
            Assert.That(_bybit.Requests.Single().Query, Does.Contain("limit=200"));
        });
    }

    [Test]
    public async Task A_point_outside_the_range_is_asked_out_and_left_out()
    {
        // Bybit takes both ends as included, so the request ends a millisecond earlier; it also counts a start between two points from the
        // earlier one. A point it gave outside the range all the same is dropped.
        _bybit.RespondJson(
            BybitAnswers.OpenInterestHistory(
                "BTCUSDT",
                BybitAnswers.OpenInterestPoint("70003", Noon),
                BybitAnswers.OpenInterestPoint("70002", Noon.AddMinutes(-5)),
                BybitAnswers.OpenInterestPoint("70001", Noon.AddMinutes(-10)),
                BybitAnswers.OpenInterestPoint("70000", Noon.AddMinutes(-15))));

        IReadOnlyList<BybitOpenInterestPoint> points = await _openInterest.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                points,
                Is.EqualTo(new[] { new BybitOpenInterestPoint(Noon.AddMinutes(-10), Value: 70001m), new BybitOpenInterestPoint(Noon.AddMinutes(-5), Value: 70002m) }));
            Assert.That(
                _bybit.Requests.Single().Query,
                Does.Contain($"startTime={Noon.AddMinutes(-10).ToUnixTimeMilliseconds()}").And.Contain($"endTime={Noon.ToUnixTimeMilliseconds() - 1}"));
        });
    }

    [Test]
    public async Task A_point_that_cannot_be_one_is_left_out_and_the_rest_of_the_page_is_told()
    {
        // An open interest below zero, and a point between two steps: either would stop the loading of the contract at this page.
        _bybit.RespondJson(
            BybitAnswers.OpenInterestHistory(
                "BTCUSDT",
                BybitAnswers.OpenInterestPoint("70002", Noon.AddMinutes(-5)),
                BybitAnswers.OpenInterestPoint("70009", Noon.AddMinutes(-7)),
                BybitAnswers.OpenInterestPoint("-1", Noon.AddMinutes(-10)),
                BybitAnswers.OpenInterestPoint("70000", Noon.AddMinutes(-15))));

        IReadOnlyList<BybitOpenInterestPoint> points = await _openInterest.GetAsync(Btc, Noon.AddMinutes(-15), Noon, CancellationToken.None);

        Assert.That(
            points,
            Is.EqualTo(new[] { new BybitOpenInterestPoint(Noon.AddMinutes(-15), Value: 70000m), new BybitOpenInterestPoint(Noon.AddMinutes(-5), Value: 70002m) }));
    }

    [Test]
    public void An_answer_Bybit_refuses_gives_no_points()
    {
        _bybit.RespondJson(BybitAnswers.Error(code: 10001, "params error: Symbol Is Invalid"));

        Assert.ThrowsAsync<HttpRequestException>(() => _openInterest.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None));
    }

    [Test]
    public async Task The_points_of_a_contract_come_in_one_request_at_the_five_minute_step_oldest_first_exactly_as_Bybit_gave_them()
    {
        // Bybit gives the newest first, with the digits its history keeps.
        _bybit.RespondJson(
            BybitAnswers.OpenInterestHistory(
                "BTCUSDT",
                BybitAnswers.OpenInterestPoint("70711.87699999", Noon.AddMinutes(-5)),
                BybitAnswers.OpenInterestPoint("70772.72900000", Noon.AddMinutes(-10))));

        IReadOnlyList<BybitOpenInterestPoint> points = await _openInterest.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                points,
                Is.EqualTo(
                    new[]
                    {
                        new BybitOpenInterestPoint(Noon.AddMinutes(-10), Value: 70772.729m),
                        new BybitOpenInterestPoint(Noon.AddMinutes(-5), Value: 70711.87699999m)
                    }));
            Assert.That(_bybit.Requests.Single().AbsolutePath, Is.EqualTo("/v5/market/open-interest"));
            Assert.That(
                _bybit.Requests.Single().Query,
                Does.Contain("category=linear").And.Contain("symbol=BTCUSDT").And.Contain("intervalTime=5min"));
        });
    }

    [Test]
    public void The_request_goes_through_the_gate_of_Bybit()
    {
        _bybit.RespondJson("", HttpStatusCode.Forbidden);

        Assert.Multiple(() =>
        {
            // The ban pauses the gate, and the pause holds the next request back.
            Assert.ThrowsAsync<ExchangePausedException>(() => _openInterest.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None));
            Assert.ThrowsAsync<ExchangePausedException>(() => _openInterest.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None));
            Assert.That(_bybit.Requests, Has.Count.EqualTo(1));
        });
    }
}