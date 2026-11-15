using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Integration.Time;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class LoadingTests
{
    [SetUp]
    public void SetUp()
    {
        _time = new WatchedTimeProvider(Start);
        _loading = new Loading("funding payments", _time, NullLogger.Instance);
        _asked = [];
    }

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 10, second: 0, TimeSpan.Zero);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly InstrumentInternal Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear()).ToInternal();

    private static readonly InstrumentInternal Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH")).ToInternal();

    private WatchedTimeProvider _time = null!;
    private Loading _loading = null!;
    private List<Guid> _asked = null!;

    // A round of both instruments, one at a time unless a test asks for more, so the pages come in this order.
    private Task<LoadingRoundResult> RunRoundAsync(Func<InstrumentInternal, Task<bool>> loadPage, int parallelism = 1)
    {
        return _loading.RunRoundAsync(
            [Btc, Eth],
            parallelism,
            (instrument, _) =>
            {
                lock (_asked)
                {
                    _asked.Add(instrument.Id);
                }

                return loadPage(instrument);
            },
            CancellationToken.None);
    }

    [Test]
    public async Task A_new_loading_asks_an_instrument_that_failed_in_the_one_before()
    {
        await RunRoundAsync(instrument => instrument == Btc ? throw new InvalidOperationException("The database is away.") : Task.FromResult(false));
        _asked.Clear();
        _loading = new Loading("funding payments", _time, NullLogger.Instance);

        await RunRoundAsync(_ => Task.FromResult(false));

        Assert.That(_asked, Is.EqualTo(new[] { Btc.Id, Eth.Id }));
    }

    [Test]
    public async Task A_pause_of_the_exchange_is_waited_out_by_the_page_that_met_it_and_its_instrument_has_more_left()
    {
        Task<LoadingRoundResult> round = RunRoundAsync(instrument =>
            instrument == Btc
                ? throw new ExchangePausedException("Bybit limited the requests.", Start.AddSeconds(30))
                : Task.FromResult(false));

        TimeSpan pause = await _time.NextWaitAsync();
        bool askedInPause = _asked.Contains(Eth.Id);
        _time.Advance(pause);
        LoadingRoundResult result = await round.WaitAsync(Patience);

        Assert.Multiple(() =>
        {
            Assert.That(pause, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(askedInPause, Is.False);

            // The round goes on with the other instrument; the paused one is asked again by the next round.
            Assert.That(_asked, Is.EqualTo(new[] { Btc.Id, Eth.Id }));
            Assert.That(result, Is.EqualTo(LoadingRoundResult.MoreLeft));
        });
    }

    [Test]
    public async Task A_round_tells_whether_any_instrument_has_more_left()
    {
        LoadingRoundResult oneHasMore = await RunRoundAsync(instrument => Task.FromResult(instrument == Eth));
        LoadingRoundResult noneHasMore = await RunRoundAsync(_ => Task.FromResult(false));

        Assert.Multiple(() =>
        {
            Assert.That(oneHasMore, Is.EqualTo(LoadingRoundResult.MoreLeft));
            Assert.That(noneHasMore, Is.EqualTo(LoadingRoundResult.NothingLeft));
            Assert.That(_asked, Is.EqualTo(new[] { Btc.Id, Eth.Id, Btc.Id, Eth.Id }));
        });
    }

    [Test]
    public async Task An_exchange_that_is_unavailable_stops_the_round()
    {
        LoadingRoundResult result = await RunRoundAsync(_ => throw new TransientExchangeException("Bybit answered 502."));

        Assert.Multiple(() =>
        {
            // The other instrument would fail the same way after retries of its own.
            Assert.That(result, Is.EqualTo(LoadingRoundResult.Unavailable));
            Assert.That(_asked, Is.EqualTo(new[] { Btc.Id }));
        });
    }

    [Test]
    public async Task An_instrument_whose_page_failed_does_not_stop_the_others_and_is_left_alone_for_the_rest_of_the_loading()
    {
        LoadingRoundResult first = await RunRoundAsync(instrument =>
            instrument == Btc ? throw new InvalidOperationException("The database is away.") : Task.FromResult(true));
        LoadingRoundResult second = await RunRoundAsync(_ => Task.FromResult(false));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(LoadingRoundResult.MoreLeft));
            Assert.That(second, Is.EqualTo(LoadingRoundResult.NothingLeft));

            // Asked again every round, it would fail the same way every time.
            Assert.That(_asked, Is.EqualTo(new[] { Btc.Id, Eth.Id, Eth.Id }));
        });
    }

    [Test]
    public async Task Pages_of_several_instruments_are_asked_at_the_same_time()
    {
        // No page is answered until both are asked: one instrument at a time would never ask for the second.
        var bothAsked = new TaskCompletionSource();
        var asked = 0;

        LoadingRoundResult result = await RunRoundAsync(
            async _ =>
            {
                if (Interlocked.Increment(ref asked) == 2)
                {
                    bothAsked.SetResult();
                }

                await bothAsked.Task.WaitAsync(Patience);

                return false;
            },
            parallelism: 2);

        Assert.That(result, Is.EqualTo(LoadingRoundResult.NothingLeft));
    }
}