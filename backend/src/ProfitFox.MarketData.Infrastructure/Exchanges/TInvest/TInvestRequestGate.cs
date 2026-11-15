using Grpc.Core;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The one way of the requests of market data to T-Invest with the one token of the app: at most
///     <see cref="TInvestApiSettings.MarketDataRequestsPerMinute" /> in any minute, a sliding window, and the retries of a transient failure.
///     After the server said the limit was hit (<c>RESOURCE_EXHAUSTED</c>) none goes until the reset it named; a request then fails at once
///     with <see cref="ExchangePausedException" />, so it does not wait inside a command. gRPC tells a failure by an exception, so what
///     it means is told here, where Bybit tells it on unwrapping its answer (<c>BybitResponses</c>).
/// </summary>
internal sealed class TInvestRequestGate(IOptionsMonitor<TInvestApiSettings> settings, ExchangeRetryExecutor retries, TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new Lock();
    private readonly Queue<DateTimeOffset> _sent = new Queue<DateTimeOffset>();
    private readonly ExchangePause _pause = new ExchangePause("T-Invest", time);

    /// <exception cref="ExchangePausedException">T-Invest limited the requests, now or before the reset.</exception>
    /// <exception cref="TransientExchangeException">The retries of a transient failure ran out.</exception>
    public async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        _pause.ThrowIfActive();

        return await retries.ExecuteAsync(
            async token =>
            {
                await AcquireAsync(token);

                try
                {
                    return await request(token);
                }
                catch (RpcException exception) when (exception.StatusCode is StatusCode.ResourceExhausted)
                {
                    throw new ExchangePausedException(
                        $"T-Invest limited the requests of market data: {exception.Status.Detail}",
                        _pause.Extend(ResetOf(exception)),
                        exception);
                }
                catch (RpcException exception) when (exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal)
                {
                    throw new TransientExchangeException($"T-Invest failed a request of market data: {exception.Status.Detail}", exception);
                }
            },
            cancellationToken);
    }

    // Returns once the request may go, and counts it as sent then; a request waits before it is sent, so it never waits after it could
    // have gone.
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

                if (_sent.Count < settings.CurrentValue.MarketDataRequestsPerMinute)
                {
                    _sent.Enqueue(now);

                    return;
                }

                wait = _sent.Peek() + Window - now;
            }

            await Task.Delay(wait, time, cancellationToken);
        }
    }

    // The server names the seconds to the reset of the window in a trailer; without it the window is waited out.
    private static TimeSpan ResetOf(RpcException exception)
    {
        return int.TryParse(exception.Trailers.GetValue("x-ratelimit-reset"), out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : Window;
    }
}