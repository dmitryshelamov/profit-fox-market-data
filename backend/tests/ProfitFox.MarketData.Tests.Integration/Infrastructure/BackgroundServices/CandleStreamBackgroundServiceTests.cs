using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Integration.Time;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class CandleStreamBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _saved = Channel.CreateUnbounded<SaveClosedCandlesCommand>();
        _stream = new StreamStub();
        _feed = Substitute.For<ILiveCandleFeed>();
        _told = Channel.CreateUnbounded<LiveCandle>();
        _feed.Publish(Arg.Do<IReadOnlyCollection<LiveCandle>>(candles =>
        {
            foreach (LiveCandle candle in candles)
            {
                _told.Writer.TryWrite(candle);
            }
        }));
        _time = new WatchedTimeProvider(Start);
        _sender = Substitute.For<ISender>();
        BybitHas(Btc, Eth, BtcSpot, Delisted);

        // Every command is recorded.
        _sender.Send(Arg.Any<SaveClosedCandlesCommand>(), Arg.Any<CancellationToken>()).Returns(Recorded);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_service is not null)
        {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 30, TimeSpan.Zero);

    private static readonly DateTimeOffset Minute = Start.AddSeconds(-30);

    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Batch = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private static readonly Instrument BtcSpot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());

    private static readonly Instrument Delisted = Delist(InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA")));

    private Channel<SaveClosedCandlesCommand> _saved = null!;
    private StreamStub _stream = null!;
    private ILiveCandleFeed _feed = null!;
    private Channel<LiveCandle> _told = null!;
    private WatchedTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider? _services;
    private CandleStreamBackgroundService? _service;

    // The settings as the app binds them; the sender is the last ISender registered, so it wins over the one of MediatR.
    private async Task StartAsync(bool bybit = true)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["CandleStream:RefreshSeconds"] = "60",
                    ["CandleStream:BatchSeconds"] = "3",
                    ["CandleStream:Bybit:Enabled"] = bybit.ToString()
                })
            .Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new CandleStreamBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _stream,
            _feed,
            _services.GetRequiredService<IOptionsMonitor<CandleStreamSettings>>(),
            _time,
            NullLogger<CandleStreamBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
    }

    private void BybitHas(params Instrument[] instruments)
    {
        _sender.Send(new GetInstrumentsQuery(Exchange.Bybit), Arg.Any<CancellationToken>())
            .Returns([.. instruments.Select(instrument => instrument.ToInternal())]);
    }

    private Task<SaveClosedCandlesResult> Recorded(NSubstitute.Core.CallInfo call)
    {
        if (call[0] is not SaveClosedCandlesCommand command)
        {
            return Task.FromResult(new SaveClosedCandlesResult(Saved: 0));
        }

        _saved.Writer.TryWrite(command);

        return Task.FromResult(new SaveClosedCandlesResult(command.Candles.Count));
    }

    private async Task<IReadOnlyCollection<ClosedCandle>> SavedAsync()
    {
        return (await _saved.Reader.ReadAsync().AsTask().WaitAsync(Patience)).Candles;
    }

    // Lets the window of the batch that waits pass; a wait of another length that started before it is the one for the catalog.
    private async Task PassBatchWindowAsync()
    {
        while (await _time.NextWaitAsync() != Batch)
        {
        }

        _time.Advance(Batch);
    }

    private static Instrument Delist(Instrument instrument)
    {
        instrument.Delist(Start.AddDays(-1));

        return instrument;
    }

    /// <summary>
    ///     The stream of Bybit for a test: it records every subscription with the listener it was made with and takes it, unless the test
    ///     had it refuse the next one.
    /// </summary>
    private sealed class StreamStub : IBybitCandleStreamClient
    {
        private readonly Channel<Subscription> _subscriptions = Channel.CreateUnbounded<Subscription>();

        public bool RefusesNext { get; set; }

        /// <summary>
        ///     A category whose subscription never completes, as one with hundreds of requests still going.
        /// </summary>
        public BybitCategory? Hangs { get; set; }

        public Task<IAsyncDisposable> SubscribeAsync(
            BybitCategory category,
            IReadOnlyCollection<string> symbols,
            Action<string, Candle> closed,
            Action<string, Candle> forming,
            CancellationToken cancellationToken)
        {
            if (RefusesNext)
            {
                RefusesNext = false;

                return Task.FromException<IAsyncDisposable>(new TransientExchangeException("Bybit did not take the subscription."));
            }

            if (category == Hangs)
            {
                return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<IAsyncDisposable>(
                    _ => throw new OperationCanceledException(cancellationToken),
                    TaskScheduler.Default);
            }

            var subscription = new Subscription(category, [.. symbols], closed, forming);
            _subscriptions.Writer.TryWrite(subscription);

            return Task.FromResult<IAsyncDisposable>(subscription);
        }

        public async Task<Subscription> NextAsync()
        {
            return await _subscriptions.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        }

        public bool HasNext()
        {
            return _subscriptions.Reader.TryPeek(out _);
        }
    }

    private sealed class Subscription(BybitCategory category, string[] symbols, Action<string, Candle> closed, Action<string, Candle> forming)
        : IAsyncDisposable
    {
        public BybitCategory Category { get; } = category;

        public string[] Symbols { get; } = symbols;

        public Action<string, Candle> Closed { get; } = closed;

        public Action<string, Candle> Forming { get; } = forming;

        public bool Ended { get; private set; }

        public ValueTask DisposeAsync()
        {
            Ended = true;

            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task A_batch_that_fails_to_save_does_not_stop_the_ones_after_it()
    {
        Candle second = CandleGenerator.Generate(Minute.AddMinutes(1));
        var failed = new TaskCompletionSource();
        _sender.Send(Arg.Any<SaveClosedCandlesCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    failed.SetResult();

                    return Task.FromException<SaveClosedCandlesResult>(new InvalidOperationException("Storage is down."));
                },
                Recorded);
        await StartAsync();
        Subscription linear = await _stream.NextAsync();

        linear.Closed("BTCUSDT", CandleGenerator.Generate(Minute));
        await PassBatchWindowAsync();

        // The first batch is taken by now: a candle told before that would join it and fail with it.
        await failed.Task.WaitAsync(Patience);
        linear.Closed("BTCUSDT", second);
        await PassBatchWindowAsync();

        Assert.That(await SavedAsync(), Is.EqualTo(new[] { new ClosedCandle(Exchange.Bybit, Btc.Id, second) }));
    }

    [Test]
    public async Task A_candle_that_comes_after_a_batch_was_saved_goes_with_the_next_one()
    {
        Candle first = CandleGenerator.Generate(Minute);
        Candle again = CandleGenerator.Generate(Minute, buyVolume: 1m);
        await StartAsync();
        Subscription linear = await _stream.NextAsync();

        linear.Closed("BTCUSDT", first);
        await PassBatchWindowAsync();
        IReadOnlyCollection<ClosedCandle> firstBatch = await SavedAsync();
        linear.Closed("BTCUSDT", again);
        await PassBatchWindowAsync();
        IReadOnlyCollection<ClosedCandle> nextBatch = await SavedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstBatch, Is.EqualTo(new[] { new ClosedCandle(Exchange.Bybit, Btc.Id, first) }));
            Assert.That(nextBatch, Is.EqualTo(new[] { new ClosedCandle(Exchange.Bybit, Btc.Id, again) }));
        });
    }

    [Test]
    public async Task A_category_still_subscribing_does_not_hold_back_the_other()
    {
        _stream.Hangs = BybitCategory.Linear;

        await StartAsync();

        Assert.That((await _stream.NextAsync()).Category, Is.EqualTo(BybitCategory.Spot));
    }

    [Test]
    public async Task A_category_Bybit_did_not_take_is_subscribed_to_after_the_wait()
    {
        BybitHas(Btc);
        _stream.RefusesNext = true;
        await StartAsync();

        TimeSpan wait = await _time.NextWaitAsync();
        bool subscribedBeforeTheWait = _stream.HasNext();
        _time.Advance(wait);
        Subscription subscription = await _stream.NextAsync();

        Assert.Multiple(() =>
        {
            Assert.That(wait, Is.EqualTo(Refresh));
            Assert.That(subscribedBeforeTheWait, Is.False);
            Assert.That(subscription.Symbols, Is.EqualTo(new[] { "BTCUSDT" }));
        });
    }

    [Test]
    public async Task The_candles_that_close_within_the_window_are_saved_together_each_for_the_instrument_of_its_category_and_symbol()
    {
        Candle candle = CandleGenerator.Generate(Minute, volume: 3m, buyVolume: 2m);
        Candle ofEth = CandleGenerator.Generate(Minute, close: 6m);
        await StartAsync();
        Subscription linear = await _stream.NextAsync();
        Subscription spot = await _stream.NextAsync();

        spot.Closed("BTCUSDT", candle);
        linear.Closed("BTCUSDT", candle);
        linear.Closed("ETHUSDT", ofEth);
        bool savedBeforeTheWindow = _saved.Reader.TryPeek(out _);
        await PassBatchWindowAsync();
        IReadOnlyCollection<ClosedCandle> saved = await SavedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(savedBeforeTheWindow, Is.False);
            Assert.That(
                saved,
                Is.EqualTo(
                    new[]
                    {
                        new ClosedCandle(Exchange.Bybit, BtcSpot.Id, candle),
                        new ClosedCandle(Exchange.Bybit, Btc.Id, candle),
                        new ClosedCandle(Exchange.Bybit, Eth.Id, ofEth)
                    }));
            Assert.That(_saved.Reader.TryPeek(out _), Is.False);
        });
    }

    [Test]
    public async Task A_forming_candle_goes_to_the_feed_at_once_and_is_not_saved()
    {
        Candle candle = CandleGenerator.Generate(Minute, volume: 3m, buyVolume: 2m);
        await StartAsync();
        await _stream.NextAsync();
        Subscription spot = await _stream.NextAsync();

        spot.Forming("BTCUSDT", candle);

        LiveCandle told = await _told.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        Assert.Multiple(() =>
        {
            Assert.That(told, Is.EqualTo(new LiveCandle(Exchange.Bybit, BtcSpot.Id, candle, Closed: false)));
            Assert.That(_saved.Reader.TryPeek(out _), Is.False);
        });
    }

    [Test]
    public async Task A_stream_that_is_off_subscribes_to_nothing()
    {
        await StartAsync(bybit: false);

        await _time.NextWaitAsync();

        Assert.That(_stream.HasNext(), Is.False);
    }

    [Test]
    public async Task An_instrument_listed_since_is_subscribed_to_after_the_wait_and_the_others_are_not_again()
    {
        BybitHas(Btc);
        await StartAsync();
        await _stream.NextAsync();

        BybitHas(Btc, Eth);
        await _time.PassNextWaitAsync();
        Subscription later = await _stream.NextAsync();
        await _time.NextWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(later.Category, Is.EqualTo(BybitCategory.Linear));
            Assert.That(later.Symbols, Is.EqualTo(new[] { "ETHUSDT" }));
            Assert.That(_stream.HasNext(), Is.False);
        });
    }

    [Test]
    public async Task The_listed_instruments_are_subscribed_to_each_category_in_a_subscription_of_its_own()
    {
        await StartAsync();

        Subscription[] subscriptions = [await _stream.NextAsync(), await _stream.NextAsync()];
        await _time.NextWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(subscriptions.Select(subscription => subscription.Category), Is.EqualTo(new[] { BybitCategory.Linear, BybitCategory.Spot }));
            Assert.That(subscriptions[0].Symbols, Is.EquivalentTo(new[] { "BTCUSDT", "ETHUSDT" }));
            Assert.That(subscriptions[1].Symbols, Is.EqualTo(new[] { "BTCUSDT" }));
            Assert.That(_stream.HasNext(), Is.False);
        });
    }

    [Test]
    public async Task The_subscriptions_end_when_the_service_stops()
    {
        await StartAsync();
        Subscription[] subscriptions = [await _stream.NextAsync(), await _stream.NextAsync()];
        await _time.NextWaitAsync();

        await _service!.StopAsync(CancellationToken.None);

        Assert.That(subscriptions.Select(subscription => subscription.Ended), Is.All.True);
    }
}