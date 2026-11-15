using System.Net;

using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.LongShortRatio;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitLongShortRatioClientTests
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
        _ratio = new BybitLongShortRatioClient(
            new BybitRestClient(
                new HttpClient(_bybit),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            _gate,
            NullLogger<BybitLongShortRatioClient>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _bybit.Dispose();
    }

    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly BybitListedInstrument Btc = BybitListedInstrumentGenerator.GenerateLinear();

    private StubHttpHandler _bybit = null!;
    private BybitRequestGate _gate = null!;
    private BybitLongShortRatioClient _ratio = null!;

    [Test]
    public void A_contract_Bybit_keeps_no_ratio_of_gives_no_points()
    {
        // As Bybit answers for a contract quoted in USDC.
        _bybit.RespondJson(BybitAnswers.Error(code: 10001, "params error: symbol not support"));

        Assert.ThrowsAsync<HttpRequestException>(() => _ratio.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None));
    }

    [Test]
    public async Task A_contract_asked_for_the_time_before_its_launch_has_no_points()
    {
        _bybit.RespondJson(BybitAnswers.LongShortRatioHistory());

        IReadOnlyList<BybitLongShortRatioPoint> points = await _ratio.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None);

        Assert.That(points, Is.Empty);
    }

    [Test]
    public async Task A_page_holds_the_five_hundred_points_Bybit_gives_at_most()
    {
        _bybit.RespondJson(BybitAnswers.LongShortRatioHistory());

        await _ratio.GetAsync(Btc, Noon - _ratio.PageLength, Noon, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_ratio.PageLength, Is.EqualTo(TimeSpan.FromMinutes(2500)));
            Assert.That(_bybit.Requests.Single().Query, Does.Contain("limit=500"));
        });
    }

    [Test]
    public async Task A_point_outside_the_range_is_asked_out_and_left_out()
    {
        // Bybit takes both ends as included, so the request ends a millisecond earlier. A point it gave outside the range all the same
        // is dropped.
        _bybit.RespondJson(
            BybitAnswers.LongShortRatioHistory(
                BybitAnswers.LongShortRatioPoint("0.7003", "0.2997", Noon),
                BybitAnswers.LongShortRatioPoint("0.7002", "0.2998", Noon.AddMinutes(-5)),
                BybitAnswers.LongShortRatioPoint("0.7001", "0.2999", Noon.AddMinutes(-10)),
                BybitAnswers.LongShortRatioPoint("0.7000", "0.3000", Noon.AddMinutes(-15))));

        IReadOnlyList<BybitLongShortRatioPoint> points = await _ratio.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                points,
                Is.EqualTo(
                    new[]
                    {
                        new BybitLongShortRatioPoint(Noon.AddMinutes(-10), Long: 0.7001m, Short: 0.2999m),
                        new BybitLongShortRatioPoint(Noon.AddMinutes(-5), Long: 0.7002m, Short: 0.2998m)
                    }));
            Assert.That(
                _bybit.Requests.Single().Query,
                Does.Contain($"startTime={Noon.AddMinutes(-10).ToUnixTimeMilliseconds()}").And.Contain($"endTime={Noon.ToUnixTimeMilliseconds() - 1}"));
        });
    }

    [Test]
    public async Task A_point_that_cannot_be_one_is_left_out_and_the_rest_of_the_page_is_told()
    {
        // A share below zero, and a point between two steps: either would stop the loading of the contract at this page.
        _bybit.RespondJson(
            BybitAnswers.LongShortRatioHistory(
                BybitAnswers.LongShortRatioPoint("0.7002", "0.2998", Noon.AddMinutes(-5)),
                BybitAnswers.LongShortRatioPoint("0.7009", "0.2991", Noon.AddMinutes(-7)),
                BybitAnswers.LongShortRatioPoint("-0.1", "1.1", Noon.AddMinutes(-10)),
                BybitAnswers.LongShortRatioPoint("0.7000", "0.3000", Noon.AddMinutes(-15))));

        IReadOnlyList<BybitLongShortRatioPoint> points = await _ratio.GetAsync(Btc, Noon.AddMinutes(-15), Noon, CancellationToken.None);

        Assert.That(
            points,
            Is.EqualTo(
                new[]
                {
                    new BybitLongShortRatioPoint(Noon.AddMinutes(-15), Long: 0.7m, Short: 0.3m),
                    new BybitLongShortRatioPoint(Noon.AddMinutes(-5), Long: 0.7002m, Short: 0.2998m)
                }));
    }

    [Test]
    public async Task The_points_of_a_contract_come_in_one_request_at_the_five_minute_step_oldest_first_exactly_as_Bybit_gave_them()
    {
        // Bybit gives the newest first.
        _bybit.RespondJson(
            BybitAnswers.LongShortRatioHistory(
                BybitAnswers.LongShortRatioPoint("0.7302", "0.2698", Noon.AddMinutes(-5)),
                BybitAnswers.LongShortRatioPoint("0.726", "0.274", Noon.AddMinutes(-10))));

        IReadOnlyList<BybitLongShortRatioPoint> points = await _ratio.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                points,
                Is.EqualTo(
                    new[]
                    {
                        new BybitLongShortRatioPoint(Noon.AddMinutes(-10), Long: 0.726m, Short: 0.274m),
                        new BybitLongShortRatioPoint(Noon.AddMinutes(-5), Long: 0.7302m, Short: 0.2698m)
                    }));
            Assert.That(_bybit.Requests.Single().AbsolutePath, Is.EqualTo("/v5/market/account-ratio"));
            Assert.That(
                _bybit.Requests.Single().Query,
                Does.Contain("category=linear").And.Contain("symbol=BTCUSDT").And.Contain("period=5min"));
        });
    }

    [Test]
    public void The_request_goes_through_the_gate_of_Bybit()
    {
        _bybit.RespondJson("", HttpStatusCode.Forbidden);

        Assert.Multiple(() =>
        {
            // The ban pauses the gate, and the pause holds the next request back.
            Assert.ThrowsAsync<ExchangePausedException>(() => _ratio.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None));
            Assert.ThrowsAsync<ExchangePausedException>(() => _ratio.GetAsync(Btc, Noon.AddMinutes(-10), Noon, CancellationToken.None));
            Assert.That(_bybit.Requests, Has.Count.EqualTo(1));
        });
    }
}