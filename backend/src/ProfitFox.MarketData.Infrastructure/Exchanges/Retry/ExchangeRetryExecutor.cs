using Polly;
using Polly.Retry;

using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

/// <summary>
///     Retries a <see cref="TransientExchangeException" /> after 1, 2, 4, 8 and 16 s, the last one propagating; any other failure goes at
///     once. The delays run on <see cref="TimeProvider" />, so tests do not wait for real time.
/// </summary>
internal sealed class ExchangeRetryExecutor(TimeProvider time)
{
    private static readonly TimeSpan[] Delays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)];

    private readonly ResiliencePipeline _retry = new ResiliencePipelineBuilder { TimeProvider = time }
        .AddRetry(
            new RetryStrategyOptions
            {
                MaxRetryAttempts = Delays.Length,
                DelayGenerator = arguments => ValueTask.FromResult<TimeSpan?>(Delays[arguments.AttemptNumber]),
                ShouldHandle = new PredicateBuilder().Handle<TransientExchangeException>()
            })
        .Build();

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        return await _retry.ExecuteAsync(async token => await action(token), cancellationToken);
    }
}