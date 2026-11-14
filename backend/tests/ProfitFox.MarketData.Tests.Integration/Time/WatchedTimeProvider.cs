using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

namespace ProfitFox.MarketData.Tests.Integration.Time;

/// <summary>
///     A fake clock that tells when the code under test starts a wait on it, so a test moves the clock on only once the wait is really
///     there and never guesses with real time.
/// </summary>
internal sealed class WatchedTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly Channel<TimeSpan> _waits = Channel.CreateUnbounded<TimeSpan>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = base.CreateTimer(callback, state, dueTime, period);
        _waits.Writer.TryWrite(dueTime);

        return timer;
    }

    /// <summary>
    ///     How long the next wait started on this clock lasts; fails when none starts in five seconds of real time.
    /// </summary>
    public async Task<TimeSpan> NextWaitAsync()
    {
        return await _waits.Reader.ReadAsync().AsTask().WaitAsync(Patience);
    }

    /// <summary>
    ///     Lets the next wait started on this clock pass.
    /// </summary>
    public async Task PassNextWaitAsync()
    {
        Advance(await NextWaitAsync());
    }
}