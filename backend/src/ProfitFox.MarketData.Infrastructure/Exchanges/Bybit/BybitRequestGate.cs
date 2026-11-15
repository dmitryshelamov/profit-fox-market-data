using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The one way of the requests of the app to Bybit, the catalog, the candles, the tickers, the funding payments and the open
///     interest alike, as Bybit limits them per IP: at most <see cref="BybitApiSettings.RequestsPerSecond" /> attempts in any second, a
///     sliding window, the retries of a transient failure, and none at all for a pause after Bybit limited them. A request during a pause
///     fails at once with <see cref="ExchangePausedException" />, so it does not wait inside a command. What a failed answer of Bybit
///     means is told by <see cref="BybitResponses" />, where the answer is unwrapped.
/// </summary>
internal sealed class BybitRequestGate(IOptionsMonitor<BybitApiSettings> settings, ExchangeRetryExecutor retries, TimeProvider time)
{
    // Bybit counts its limit in a rolling second, so the requests that waited for a pause or a quiet time may not go all at once.
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new Lock();
    private readonly Queue<DateTimeOffset> _sent = new Queue<DateTimeOffset>();
    private readonly ExchangePause _pause = new ExchangePause("Bybit", time);
    private TimeSpan _lastLimitPause = TimeSpan.Zero;

    /// <exception cref="ExchangePausedException">Bybit limited the requests, now or before the pause ended.</exception>
    /// <exception cref="TransientExchangeException">The retries of a transient failure ran out.</exception>
    public async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        _pause.ThrowIfActive();

        return await retries.ExecuteAsync(
            async token =>
            {
                // A pause that begins while the request waits stops it there.
                await AcquireAsync(token);

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

    // Returns once the request may go, and counts it as sent then; the limit is read each time, so a new one holds at once.
    private async Task AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            _pause.ThrowIfActive();

            TimeSpan wait;
            DateTimeOffset now = time.GetUtcNow();

            lock (_lock)
            {
                while (_sent.Count > 0 && _sent.Peek() <= now - Window)
                {
                    _sent.Dequeue();
                }

                if (_sent.Count < settings.CurrentValue.RequestsPerSecond)
                {
                    _sent.Enqueue(now);

                    return;
                }

                wait = _sent.Peek() + Window - now;
            }

            await Task.Delay(wait, time, cancellationToken);
        }
    }

    // A ban pauses for the ban pause. The first answer about the limit pauses for the limit pause, each next one in a row twice as long, up
    // to the ban pause; a request that goes through starts the ladder again.
    private DateTimeOffset Pause(bool ipBanned)
    {
        BybitApiSettings current = settings.CurrentValue;
        var banPause = TimeSpan.FromMinutes(current.BanPauseMinutes);

        if (ipBanned)
        {
            return _pause.Extend(banPause);
        }

        lock (_lock)
        {
            TimeSpan next = _lastLimitPause == TimeSpan.Zero ? TimeSpan.FromSeconds(current.LimitPauseSeconds) : _lastLimitPause * 2;
            _lastLimitPause = next < banPause ? next : banPause;

            return _pause.Extend(_lastLimitPause);
        }
    }
}