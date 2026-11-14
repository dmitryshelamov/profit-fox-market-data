using Grpc.Core;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Integration.Time;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.TInvest;

[TestFixture]
public class TInvestRequestGateTests
{
    [SetUp]
    public void SetUp()
    {
        _time = new WatchedTimeProvider(Now);
        _gate = new TInvestRequestGate(
            new FixedOptionsMonitor<TInvestApiSettings>(new TInvestApiSettings { MarketDataRequestsPerMinute = 2 }),
            new ExchangeRetryExecutor(_time),
            _time);
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private WatchedTimeProvider _time = null!;
    private TInvestRequestGate _gate = null!;

    [Test]
    public void A_failure_that_does_not_pass_by_itself_fails_at_once()
    {
        var attempts = 0;

        RpcException? failure = Assert.ThrowsAsync<RpcException>(() => _gate.SendAsync<int>(
            _ =>
            {
                attempts++;

                throw new RpcException(new Status(StatusCode.InvalidArgument, "bad instrument"));
            },
            CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
            Assert.That(attempts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_limit_pauses_until_the_reset_the_server_named_and_a_request_then_fails_at_once()
    {
        ExchangePausedException? paused = Assert.ThrowsAsync<ExchangePausedException>(() => _gate.SendAsync<int>(
            _ => throw new RpcException(new Status(StatusCode.ResourceExhausted, "limit"), new Metadata { { "x-ratelimit-reset", "15" } }),
            CancellationToken.None));
        var sent = 0;

        Assert.ThrowsAsync<ExchangePausedException>(() => _gate.SendAsync(_ => Task.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(15));
        int result = await _gate.SendAsync(_ => Task.FromResult(++sent), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(paused!.Until, Is.EqualTo(Now.AddSeconds(15)));
            Assert.That(result, Is.EqualTo(1));
        });
    }

    [Test]
    public void A_limit_without_a_reset_pauses_for_a_minute()
    {
        ExchangePausedException? paused = Assert.ThrowsAsync<ExchangePausedException>(() => _gate.SendAsync<int>(
            _ => throw new RpcException(new Status(StatusCode.ResourceExhausted, "limit")),
            CancellationToken.None));

        Assert.That(paused!.Until, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public async Task An_unavailable_server_is_retried()
    {
        var attempts = 0;
        Task<int> sending = _gate.SendAsync(
            _ => ++attempts < 3 ? throw new RpcException(new Status(StatusCode.Unavailable, "down")) : Task.FromResult(attempts),
            CancellationToken.None);

        // The waits before the two retries, then the wait of the third attempt for the window of a minute, which lets two go.
        await _time.PassNextWaitAsync();
        await _time.PassNextWaitAsync();
        await _time.PassNextWaitAsync();

        Assert.That(await sending.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(3));
    }

    [Test]
    public async Task No_more_requests_go_in_a_minute_than_the_settings_let()
    {
        await _gate.SendAsync(_ => Task.FromResult(0), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(20));
        await _gate.SendAsync(_ => Task.FromResult(0), CancellationToken.None);

        // The third waits until the first, sent 20 s ago, leaves the window of a minute.
        Task<int> third = _gate.SendAsync(_ => Task.FromResult(3), CancellationToken.None);
        TimeSpan wait = await _time.NextWaitAsync();
        _time.Advance(wait);

        Assert.Multiple(async () =>
        {
            Assert.That(wait, Is.EqualTo(TimeSpan.FromSeconds(40)));
            Assert.That(await third.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(3));
        });
    }
}