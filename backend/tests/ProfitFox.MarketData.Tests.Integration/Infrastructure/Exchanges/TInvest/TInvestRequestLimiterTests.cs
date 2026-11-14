using Grpc.Core;

using Microsoft.Extensions.Time.Testing;

using ProfitFox.MarketData.Infrastructure.Exchanges;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Integration.Time;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.TInvest;

[TestFixture]
public class TInvestRequestLimiterTests
{
    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Now);
        _limiter = new TInvestRequestLimiter(
            new FixedOptionsMonitor<TInvestApiSettings>(new TInvestApiSettings { MarketDataRequestsPerMinute = 2 }),
            new ExchangeRetryExecutor(_time),
            _time);
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private FakeTimeProvider _time = null!;
    private TInvestRequestLimiter _limiter = null!;

    [Test]
    public void A_failure_that_does_not_pass_by_itself_fails_at_once()
    {
        var attempts = 0;

        RpcException? failure = Assert.ThrowsAsync<RpcException>(() => _limiter.SendAsync<int>(
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
        ExchangePausedException? paused = Assert.ThrowsAsync<ExchangePausedException>(() => _limiter.SendAsync<int>(
            _ => throw new RpcException(new Status(StatusCode.ResourceExhausted, "limit"), new Metadata { { "x-ratelimit-reset", "15" } }),
            CancellationToken.None));
        var sent = 0;

        Assert.ThrowsAsync<ExchangePausedException>(() => _limiter.SendAsync(_ => Task.FromResult(++sent), CancellationToken.None));
        _time.Advance(TimeSpan.FromSeconds(15));
        int result = await _limiter.SendAsync(_ => Task.FromResult(++sent), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(paused!.Until, Is.EqualTo(Now.AddSeconds(15)));
            Assert.That(result, Is.EqualTo(1));
        });
    }

    [Test]
    public void A_limit_without_a_reset_pauses_for_a_minute()
    {
        ExchangePausedException? paused = Assert.ThrowsAsync<ExchangePausedException>(() => _limiter.SendAsync<int>(
            _ => throw new RpcException(new Status(StatusCode.ResourceExhausted, "limit")),
            CancellationToken.None));

        Assert.That(paused!.Until, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public async Task An_unavailable_server_is_retried()
    {
        var attempts = 0;
        Task<int> sending = _limiter.SendAsync(
            _ => ++attempts < 3 ? throw new RpcException(new Status(StatusCode.Unavailable, "down")) : Task.FromResult(attempts),
            CancellationToken.None);

        await _time.AdvanceUntilDoneAsync(sending, TimeSpan.FromSeconds(1));

        Assert.That(await sending, Is.EqualTo(3));
    }

    [Test]
    public async Task No_more_requests_go_in_a_minute_than_the_settings_let()
    {
        await _limiter.SendAsync(_ => Task.FromResult(0), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(20));
        await _limiter.SendAsync(_ => Task.FromResult(0), CancellationToken.None);
        Task<int> third = _limiter.SendAsync(_ => Task.FromResult(3), CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(50));
        bool waited = !third.IsCompleted;
        _time.Advance(TimeSpan.FromSeconds(40));

        Assert.Multiple(async () =>
        {
            Assert.That(waited, Is.True);
            Assert.That(await third.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(3));
        });
    }
}