using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The one way of the requests of the app to Bybit, the catalog and the candles alike, as Bybit limits them per IP: at most
///     <see cref="BybitApiSettings.RequestsPerSecond" /> attempts a second, the retries of a transient failure, and none at all for a pause
///     after Bybit limited them. A request during a pause fails at once with <see cref="ExchangePausedException" />, so it does not wait
///     inside the transaction of a command. The pause is kept in memory only.
/// </summary>
internal sealed class BybitRequestGate : IDisposable
{
    private readonly IOptionsMonitor<BybitApiSettings> _settings;
    private readonly ExchangeRetryExecutor _retries;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _tokens;
    private readonly ITimer _refill;
    private readonly Lock _lock = new Lock();
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;
    private TimeSpan _lastLimitPause = TimeSpan.Zero;

    public BybitRequestGate(IOptionsMonitor<BybitApiSettings> settings, ExchangeRetryExecutor retries, TimeProvider time)
    {
        _settings = settings;
        _retries = retries;
        _time = time;

        // A token bucket on the clock of the app, so tests do not wait for real time: only the timer adds tokens, each second up to the
        // limit it reads then, so a new limit holds from the next second and a fill never goes over it.
        _tokens = new SemaphoreSlim(settings.CurrentValue.RequestsPerSecond);
        _refill = time.CreateTimer(
            _ =>
            {
                int missing = _settings.CurrentValue.RequestsPerSecond - _tokens.CurrentCount;

                if (missing > 0)
                {
                    _tokens.Release(missing);
                }
            },
            state: null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        _refill.Dispose();
        _tokens.Dispose();
    }

    /// <exception cref="ExchangePausedException">Bybit limited the requests, now or before the pause ended.</exception>
    /// <exception cref="TransientExchangeException">The retries of a transient failure ran out.</exception>
    public async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        ThrowIfPaused();

        return await _retries.ExecuteAsync(
            async token =>
            {
                await _tokens.WaitAsync(token);

                // The pause may have begun while the request waited for its token.
                ThrowIfPaused();

                try
                {
                    T result = await request(token);

                    lock (_lock)
                    {
                        _lastLimitPause = TimeSpan.Zero;
                    }

                    return result;
                }
                catch (ExchangeRateLimitedException exception)
                {
                    throw new ExchangePausedException($"Bybit limited the requests: {exception.Message}", Pause(exception.IpBanned), exception);
                }
            },
            cancellationToken);
    }

    private void ThrowIfPaused()
    {
        DateTimeOffset pausedUntil;

        lock (_lock)
        {
            pausedUntil = _pausedUntil;
        }

        if (_time.GetUtcNow() < pausedUntil)
        {
            throw new ExchangePausedException($"Bybit limited the requests; they wait until {pausedUntil:O}.", pausedUntil);
        }
    }

    // A ban pauses for the ban pause. The first answer about the limit pauses for the limit pause, each next one in a row twice as long, up
    // to the ban pause; a request that goes through starts the ladder again. A pause only ever ends later: a limit on a request sent
    // before a ban comes back after it and must not shorten it.
    private DateTimeOffset Pause(bool ipBanned)
    {
        BybitApiSettings settings = _settings.CurrentValue;
        var banPause = TimeSpan.FromMinutes(settings.BanPauseMinutes);

        lock (_lock)
        {
            TimeSpan pause = banPause;

            if (!ipBanned)
            {
                TimeSpan next = _lastLimitPause == TimeSpan.Zero ? TimeSpan.FromSeconds(settings.LimitPauseSeconds) : _lastLimitPause * 2;
                pause = next < banPause ? next : banPause;
                _lastLimitPause = pause;
            }

            DateTimeOffset until = _time.GetUtcNow() + pause;
            _pausedUntil = until > _pausedUntil ? until : _pausedUntil;

            return _pausedUntil;
        }
    }
}