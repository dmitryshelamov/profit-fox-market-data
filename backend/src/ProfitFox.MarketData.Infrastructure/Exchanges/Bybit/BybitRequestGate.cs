using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The one way of the requests of the app to Bybit, the catalog and the candles alike, as Bybit limits them per IP: at most
///     <see cref="BybitApiSettings.RequestsPerSecond" /> attempts a second, the retries of a transient failure, and none at all for a pause
///     after Bybit limited them. A request during a pause fails at once with <see cref="ExchangePausedException" />, so it does not wait
///     inside a command. What a failed answer of Bybit means is told by <see cref="BybitResponses" />, where the answer is unwrapped.
/// </summary>
internal sealed class BybitRequestGate : IDisposable
{
    private readonly IOptionsMonitor<BybitApiSettings> _settings;
    private readonly ExchangeRetryExecutor _retries;
    private readonly ExchangePause _pause;
    private readonly SemaphoreSlim _tokens;
    private readonly ITimer _refill;
    private readonly Lock _lock = new Lock();
    private TimeSpan _lastLimitPause = TimeSpan.Zero;

    public BybitRequestGate(IOptionsMonitor<BybitApiSettings> settings, ExchangeRetryExecutor retries, TimeProvider time)
    {
        _settings = settings;
        _retries = retries;
        _pause = new ExchangePause("Bybit", time);

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
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    /// <exception cref="ExchangePausedException">Bybit limited the requests, now or before the pause ended.</exception>
    /// <exception cref="TransientExchangeException">The retries of a transient failure ran out.</exception>
    public async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        _pause.ThrowIfActive();

        return await _retries.ExecuteAsync(
            async token =>
            {
                await _tokens.WaitAsync(token);

                // The pause may have begun while the request waited for its token.
                _pause.ThrowIfActive();

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

    public void Dispose()
    {
        _refill.Dispose();
        _tokens.Dispose();
    }

    // A ban pauses for the ban pause. The first answer about the limit pauses for the limit pause, each next one in a row twice as long, up
    // to the ban pause; a request that goes through starts the ladder again.
    private DateTimeOffset Pause(bool ipBanned)
    {
        BybitApiSettings settings = _settings.CurrentValue;
        var banPause = TimeSpan.FromMinutes(settings.BanPauseMinutes);

        if (ipBanned)
        {
            return _pause.Extend(banPause);
        }

        lock (_lock)
        {
            TimeSpan next = _lastLimitPause == TimeSpan.Zero ? TimeSpan.FromSeconds(settings.LimitPauseSeconds) : _lastLimitPause * 2;
            _lastLimitPause = next < banPause ? next : banPause;

            return _pause.Extend(_lastLimitPause);
        }
    }
}