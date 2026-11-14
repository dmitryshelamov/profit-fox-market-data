using Microsoft.Extensions.Time.Testing;

namespace ProfitFox.MarketData.Tests.Integration.Time;

internal static class FakeTimeProviderExtensions
{
    /// <summary>
    ///     Moves the clock on by <paramref name="step" /> while <paramref name="task" /> waits on it; before each step the task gets a moment
    ///     of real time to reach its next wait.
    /// </summary>
    public static async Task AdvanceUntilDoneAsync(this FakeTimeProvider time, Task task, TimeSpan step)
    {
        for (var steps = 0; steps < 1000 && !task.IsCompleted; steps++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5));

            if (!task.IsCompleted)
            {
                time.Advance(step);
            }
        }
    }
}