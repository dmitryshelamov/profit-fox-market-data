using System.Threading.Channels;

using Microsoft.Extensions.Time.Testing;

namespace ProfitFox.MarketData.Tests.Integration.Time;

/// <summary>
///     A fake clock that tells when the code under test starts a wait on it, so a test moves the clock on only once the wait is really
///     there and never guesses with real time, and when a timer of the code that repeats has run.
/// </summary>
internal sealed class WatchedTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly Channel<TimeSpan> _waits = Channel.CreateUnbounded<TimeSpan>();

    private readonly Channel<TimeSpan> _runs = Channel.CreateUnbounded<TimeSpan>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ITimer timer = base.CreateTimer(period > TimeSpan.Zero ? Watched(callback, period) : callback, state, dueTime, period);
        _waits.Writer.TryWrite(dueTime);

        return timer;
    }

    // A timer that repeats tells when a run of it is over, so a test knows what it does each time is done.
    private TimerCallback Watched(TimerCallback callback, TimeSpan period)
    {
        return ran =>
        {
            callback(ran);
            _runs.Writer.TryWrite(period);
        };
    }

    /// <summary>
    ///     How long the next wait started on this clock lasts; fails when none starts in five seconds of real time.
    /// </summary>
    public async Task<TimeSpan> NextWaitAsync()
    {
        return await _waits.Reader.ReadAsync().AsTask().WaitAsync(Patience);
    }

    /// <summary>
    ///     Lets <paramref name="period" /> pass and waits until a timer that repeats has run; fails when none has in five seconds of
    ///     real time. Moving the clock alone is not enough: the fake clock leaves a due timer unrun when another thread is waking its
    ///     timers at that moment, as code that starts a timer of its own after every message it takes is, so the clock is nudged
    ///     until the run is over.
    /// </summary>
    public async Task PassPeriodAsync(TimeSpan period)
    {
        while (_runs.Reader.TryRead(out _))
        {
        }

        Advance(period);
        DateTime until = DateTime.UtcNow + Patience;

        while (!_runs.Reader.TryRead(out _))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("No repeating timer ran.");
            }

            await Task.Delay(millisecondsDelay: 1);
            Advance(TimeSpan.Zero);
        }
    }

    /// <summary>
    ///     Lets the next wait started on this clock pass.
    /// </summary>
    public async Task PassNextWaitAsync()
    {
        Advance(await NextWaitAsync());
    }
}