using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitCandlesClientTests
{
    [SetUp]
    public void SetUp()
    {
        _bybit = new StubHttpHandler();

        // The rate limiter of the SDK is off: it would wait in real time.
        _candles = new BybitCandlesClient(
            new BybitRestClient(
                new HttpClient(_bybit),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            new FakeTimeProvider(Now));
    }

    [TearDown]
    public void TearDown()
    {
        _bybit.Dispose();
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 30, TimeSpan.Zero);

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 8, minute: 0, second: 0, TimeSpan.Zero);

    // Daily candles of Bybit start at midnight UTC.
    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly BybitListedInstrument Linear = BybitListedInstrumentGenerator.GenerateLinear();

    private static readonly BybitListedInstrument Spot = BybitListedInstrumentGenerator.GenerateSpot();

    private StubHttpHandler _bybit = null!;
    private BybitCandlesClient _candles = null!;

    private static MinuteRange Minutes(int from, int until)
    {
        return new MinuteRange(Start.AddMinutes(from), Start.AddMinutes(until));
    }

    [Test]
    public async Task A_candle_outside_the_range_is_left_out()
    {
        _bybit.RespondJson(
            BybitAnswers.Klines(
                "linear",
                Linear.Symbol,
                BybitAnswers.Kline(Start.AddMinutes(60), "1", "1", "1", "1", "1", "1"),
                BybitAnswers.Kline(Start.AddMinutes(59), "2", "2", "2", "2", "1", "2"),
                BybitAnswers.Kline(Start.AddMinutes(-1), "3", "3", "3", "3", "1", "3")));

        IReadOnlyList<Candle> candles = await _candles.GetAsync(Linear, Minutes(from: 0, until: 60), CancellationToken.None);

        Assert.That(candles.Select(candle => candle.OpenTime), Is.EqualTo(new[] { Start.AddMinutes(59) }));
    }

    [Test]
    public void A_failed_answer_fails_the_call()
    {
        _bybit.RespondJson(BybitAnswers.Error(code: 10001, "params error"));

        Assert.ThrowsAsync<HttpRequestException>(() => _candles.GetAsync(Linear, Minutes(from: 0, until: 60), CancellationToken.None));
    }

    [Test]
    public async Task A_full_page_of_daily_candles_leaves_the_start_of_a_spot_pair_unknown()
    {
        _bybit.RespondJson(
            BybitAnswers.Klines(
                "spot",
                Spot.Symbol,
                [.. Enumerable.Range(start: 0, count: 1000).Select(day => BybitAnswers.Kline(Today.AddDays(-day), "1", "1", "1", "1", "1", "1"))]));

        Assert.That(await _candles.GetTradingStartAsync(Spot, Start.AddDays(-2000), CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task A_spot_pair_without_daily_candles_has_no_known_start()
    {
        _bybit.RespondJson(BybitAnswers.Klines("spot", Spot.Symbol));

        Assert.That(await _candles.GetTradingStartAsync(Spot, Start.AddDays(-365), CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Candles_of_a_range_come_oldest_first_as_Bybit_gave_them()
    {
        _bybit.RespondJson(
            BybitAnswers.Klines(
                "linear",
                Linear.Symbol,
                BybitAnswers.Kline(Start.AddMinutes(1), "100.5", "101", "100", "100.75", "12.345", "1243.98"),
                BybitAnswers.Kline(Start, "100", "100.5", "99.5", "100.5", "0", "0")));

        IReadOnlyList<Candle> candles = await _candles.GetAsync(Linear, Minutes(from: 0, until: 1000), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                candles,
                Is.EqualTo(
                    new[]
                    {
                        new Candle(Start, open: 100m, high: 100.5m, low: 99.5m, close: 100.5m, volume: 0m, turnover: 0m),
                        new Candle(Start.AddMinutes(1), open: 100.5m, high: 101m, low: 100m, close: 100.75m, volume: 12.345m, turnover: 1243.98m)
                    }));
            Assert.That(
                _bybit.Requests.Single().Query,
                Does.Contain("category=linear")
                    .And.Contain($"symbol={Linear.Symbol}")
                    .And.Contain("interval=1&")
                    .And.Contain($"start={Start.ToUnixTimeMilliseconds()}")
                    .And.Contain($"end={Start.AddMinutes(1000).ToUnixTimeMilliseconds() - 1}")
                    .And.Contain("limit=1000"));
        });
    }

    [Test]
    public async Task The_start_of_a_linear_contract_is_its_launch_time_without_asking_Bybit()
    {
        DateTimeOffset? start = await _candles.GetTradingStartAsync(Linear, Start.AddDays(-365), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(Linear.LaunchTime));
            Assert.That(_bybit.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task The_start_of_a_spot_pair_is_the_day_of_its_first_daily_candle()
    {
        DateTimeOffset firstDay = Today.AddDays(-10);
        _bybit.RespondJson(
            BybitAnswers.Klines(
                "spot",
                Spot.Symbol,
                BybitAnswers.Kline(firstDay.AddDays(1), "1", "1", "1", "1", "1", "1"),
                BybitAnswers.Kline(firstDay, "1", "1", "1", "1", "1", "1")));

        DateTimeOffset? start = await _candles.GetTradingStartAsync(Spot, Start.AddDays(-365), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(firstDay));
            Assert.That(_bybit.Requests.Single().Query, Does.Contain("category=spot").And.Contain("interval=D"));
        });
    }
}