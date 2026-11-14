using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Integration.Time;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitRequestGateTests
{
    [SetUp]
    public async Task SetUp()
    {
        _time = new WatchedTimeProvider(Now);
        _gate = new BybitRequestGate(
            new FixedOptionsMonitor<BybitApiSettings>(new BybitApiSettings { RequestsPerSecond = 2, LimitPauseSeconds = 30, BanPauseMinutes = 2 }),
            new ExchangeRetryExecutor(_time),
            _time);

        // The timer that refills the bucket every second.
        await _time.NextWaitAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _gate.Dispose();
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private WatchedTimeProvider _time = null!;
    private BybitRequestGate _gate = null!;

    // A request Bybit limits, in the next second, so the bucket has a token for it.
    private async Task<ExchangePausedException?> PausedAsync(bool ipBanned)
    {
        _time.Advance(TimeSpan.FromSeconds(1));

        try
        {
            await _gate.SendAsync<int>(_ => throw new ExchangeRateLimitedException("Bybit limited the request.", ipBanned), CancellationToken.None);
        }
        catch (ExchangePausedException exception)
        {
            return exception;
        }

        return null;
    }

    [Test]
    public async Task A_ban_of_the_IP_pauses_for_the_ban_pause()
    {
        ExchangePausedException? paused = await PausedAsync(ipBanned: true);

        Assert.That(paused?.Until, Is.EqualTo(_time.GetUtcNow().AddMinutes(2)));
    }

    [Test]
    public async Task A_request_during_a_pause_fails_at_once_and_goes_after_it()
    {
        await PausedAsync(ipBanned: false);
        var sent = 0;

        Assert.ThrowsAsync<ExchangePausedException>(() => _gate.SendAsync(_ => Task.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(30));
        int result = await _gate.SendAsync(_ => Task.FromResult(++sent), CancellationToken.None);

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public async Task A_request_that_goes_through_starts_the_ladder_of_pauses_again()
    {
        await PausedAsync(ipBanned: false);
        _time.Advance(TimeSpan.FromSeconds(30));
        await _gate.SendAsync(_ => Task.FromResult(0), CancellationToken.None);

        ExchangePausedException? paused = await PausedAsync(ipBanned: false);

        Assert.That(paused?.Until, Is.EqualTo(_time.GetUtcNow().AddSeconds(30)));
    }

    [Test]
    public async Task A_transient_failure_is_retried_after_1_2_4_8_and_16_seconds_and_the_last_one_fails()
    {
        var attempts = new List<DateTimeOffset>();
        Task<int> sending = _gate.SendAsync<int>(
            _ =>
            {
                attempts.Add(_time.GetUtcNow());

                throw new TransientExchangeException("Bybit answered 502.");
            },
            CancellationToken.None);

        for (var retry = 0; retry < 5; retry++)
        {
            await _time.PassNextWaitAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(() => sending, Throws.InstanceOf<TransientExchangeException>());
            Assert.That(attempts.Select(attempt => (attempt - Now).TotalSeconds), Is.EqualTo(new double[] { 0, 1, 3, 7, 15, 31 }));
        });
    }

    [Test]
    public async Task Limits_in_a_row_pause_twice_as_long_each_time_up_to_the_ban_pause()
    {
        var pauses = new List<TimeSpan>();

        for (var limit = 0; limit < 4; limit++)
        {
            ExchangePausedException? paused = await PausedAsync(ipBanned: false);
            pauses.Add(paused!.Until - _time.GetUtcNow());
            _time.Advance(paused.Until - _time.GetUtcNow());
        }

        Assert.That(
            pauses,
            Is.EqualTo(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120) }));
    }

    [Test]
    public async Task No_more_requests_go_in_a_second_than_the_settings_let()
    {
        Task<int>[] sending = [.. Enumerable.Range(0, 3).Select(request => _gate.SendAsync(_ => Task.FromResult(request), CancellationToken.None))];
        await Task.WhenAll(sending[0], sending[1]).WaitAsync(TimeSpan.FromSeconds(5));

        // The bucket is empty, so the third can only go once the clock fills it again.
        bool thirdWaits = !sending[2].IsCompleted;
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Multiple(async () =>
        {
            Assert.That(thirdWaits, Is.True);
            Assert.That(await sending[2].WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(2));
        });
    }
}