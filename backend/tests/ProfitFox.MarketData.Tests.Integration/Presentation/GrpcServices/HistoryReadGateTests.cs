using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Presentation.GrpcServices;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.GrpcServices;

[TestFixture]
public class HistoryReadGateTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // Long enough for a place handed out to be taken, short enough for a test that shows none was.
    private static readonly TimeSpan Moment = TimeSpan.FromMilliseconds(200);

    [Test]
    public async Task A_call_over_the_limit_waits_until_a_place_frees()
    {
        var gate = new HistoryReadGate(new Settings(maxConcurrentReads: 2));
        IDisposable first = await gate.EnterAsync(CancellationToken.None);
        using IDisposable second = await gate.EnterAsync(CancellationToken.None);

        Task<IDisposable> third = gate.EnterAsync(CancellationToken.None);
        await Task.Delay(Moment);
        bool waited = !third.IsCompleted;
        first.Dispose();

        Assert.Multiple(async () =>
        {
            Assert.That(waited, Is.True);
            Assert.That(await third.WaitAsync(Patience), Is.Not.Null);
        });
    }

    [Test]
    public async Task A_call_that_gives_up_while_it_waits_takes_no_place()
    {
        var gate = new HistoryReadGate(new Settings(maxConcurrentReads: 1));
        IDisposable first = await gate.EnterAsync(CancellationToken.None);
        using var giveUp = new CancellationTokenSource();

        Task<IDisposable> gaveUp = gate.EnterAsync(giveUp.Token);
        Task<IDisposable> next = gate.EnterAsync(CancellationToken.None);
        await giveUp.CancelAsync();
        first.Dispose();

        Assert.Multiple(async () =>
        {
            Assert.That(() => gaveUp, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(await next.WaitAsync(Patience), Is.Not.Null);
        });
    }

    [Test]
    public async Task A_raised_limit_lets_in_more_once_a_place_frees()
    {
        var settings = new Settings(maxConcurrentReads: 1);
        var gate = new HistoryReadGate(settings);
        IDisposable first = await gate.EnterAsync(CancellationToken.None);
        Task<IDisposable> second = gate.EnterAsync(CancellationToken.None);
        Task<IDisposable> third = gate.EnterAsync(CancellationToken.None);

        settings.MaxConcurrentReads = 2;
        first.Dispose();

        Assert.That(await Task.WhenAll(second, third).WaitAsync(Patience), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task A_place_disposed_twice_frees_one_place()
    {
        var gate = new HistoryReadGate(new Settings(maxConcurrentReads: 1));
        IDisposable first = await gate.EnterAsync(CancellationToken.None);
        Task<IDisposable> second = gate.EnterAsync(CancellationToken.None);
        Task<IDisposable> third = gate.EnterAsync(CancellationToken.None);

        first.Dispose();
        first.Dispose();
        await second.WaitAsync(Patience);
        await Task.Delay(Moment);

        Assert.That(third.IsCompleted, Is.False);
    }

    // Settings a test changes while the gate reads them.
    private sealed class Settings(int maxConcurrentReads) : IOptionsMonitor<HistoryReadSettings>
    {
        public int MaxConcurrentReads { get; set; } = maxConcurrentReads;

        public HistoryReadSettings CurrentValue => new HistoryReadSettings { MaxConcurrentReads = MaxConcurrentReads };

        public HistoryReadSettings Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<HistoryReadSettings, string?> listener)
        {
            return null;
        }
    }
}