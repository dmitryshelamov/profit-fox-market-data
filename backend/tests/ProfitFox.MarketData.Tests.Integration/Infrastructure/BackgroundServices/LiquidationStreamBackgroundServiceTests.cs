using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.Core;

using ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;
using ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Liquidations;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Integration.Time;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class LiquidationStreamBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _saved = Channel.CreateUnbounded<SaveLiquidationsCommand>();
        _marked = Channel.CreateUnbounded<InvalidateLiquidationsCommand>();
        _stream = new StreamStub();
        _time = new WatchedTimeProvider(Start);
        _waits = [];
        _last = null;
        _sender = Substitute.For<ISender>();
        BybitHas(Btc, Eth, BtcSpot, Delisted);
        _sender.Send(new GetLastLiquidationTimeQuery(), Arg.Any<CancellationToken>()).Returns(_ => _last);

        // Every command is recorded.
        _sender.Send(Arg.Any<SaveLiquidationsCommand>(), Arg.Any<CancellationToken>()).Returns(Saved);
        _sender.Send(Arg.Any<InvalidateLiquidationsCommand>(), Arg.Any<CancellationToken>()).Returns(Marked);
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

    // The minute the service starts in.
    private static readonly DateTimeOffset Minute = Start.AddSeconds(-30);

    private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Save = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private static readonly Instrument BtcSpot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());

    private static readonly Instrument Delisted = Delist(InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA")));

    // The catalog first saw it 70 seconds after the minute the service starts in.
    private static readonly Instrument Sol = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("SOL"), Minute.AddSeconds(70));

    private Channel<SaveLiquidationsCommand> _saved = null!;
    private Channel<InvalidateLiquidationsCommand> _marked = null!;
    private StreamStub _stream = null!;
    private WatchedTimeProvider _time = null!;
    private List<TimeSpan> _waits = null!;
    private DateTimeOffset? _last;
    private DateTimeOffset _refreshDue;
    private ISender _sender = null!;
    private IConfigurationRoot _configuration = null!;
    private ServiceProvider? _services;
    private LiquidationStreamBackgroundService? _service;

    // The settings as the app binds them; the sender is the last ISender registered, so it wins over the one of MediatR. The first
    // pass over the catalog is over when this returns: both the subscribing and the saving wait.
    private async Task StartAsync(bool bybit = true)
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["LiquidationStream:RefreshSeconds"] = "60",
                    ["LiquidationStream:SaveSeconds"] = "5",
                    ["LiquidationStream:Bybit:Enabled"] = bybit.ToString()
                })
            .Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(_configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new LiquidationStreamBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _stream,
            _services.GetRequiredService<IOptionsMonitor<LiquidationStreamSettings>>(),
            _time,
            NullLogger<LiquidationStreamBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
        await WaitStartedAsync(Refresh);
        await WaitStartedAsync(Save);
        _refreshDue = Start + Refresh;
    }

    // Like a save of appsettings.json: the value changes and the configuration tells its readers.
    private void Change(string key, string value)
    {
        _configuration[key] = value;
        _configuration.Reload();
    }

    private void BybitHas(params Instrument[] instruments)
    {
        _sender.Send(new GetInstrumentsQuery(Exchange.Bybit), Arg.Any<CancellationToken>())
            .Returns([.. instruments.Select(instrument => instrument.ToInternal())]);
    }

    private Task Saved(CallInfo call)
    {
        if (call[0] is SaveLiquidationsCommand command)
        {
            _saved.Writer.TryWrite(command);
        }

        return Task.CompletedTask;
    }

    private Task Marked(CallInfo call)
    {
        if (call[0] is InvalidateLiquidationsCommand command)
        {
            _marked.Writer.TryWrite(command);
        }

        return Task.CompletedTask;
    }

    // Waits until the service starts a wait of this length; the waits of other lengths it started meanwhile are kept for their turn.
    private async Task WaitStartedAsync(TimeSpan length)
    {
        while (!_waits.Remove(length))
        {
            _waits.Add(await _time.NextWaitAsync());
        }
    }

    // Lets the seconds to the next save pass, five at a time, and returns once all that woke up waits again: the save is over, and so
    // is the pass over the catalog when its wait ended at the same moment. A minute passes with every 12 saves, the first of them 5
    // seconds after the start.
    private async Task PassSavesAsync(int count = 1)
    {
        for (var save = 0; save < count; save++)
        {
            _time.Advance(Save);

            if (_time.GetUtcNow() >= _refreshDue)
            {
                await WaitStartedAsync(Refresh);
                _refreshDue = _time.GetUtcNow() + Refresh;
            }

            await WaitStartedAsync(Save);
        }
    }

    // The saves sent since the last time, the minutes of each.
    private List<IReadOnlyCollection<LiquidationMinute>> TakeSaved()
    {
        var saved = new List<IReadOnlyCollection<LiquidationMinute>>();

        while (_saved.Reader.TryRead(out SaveLiquidationsCommand? command))
        {
            saved.Add(command.Minutes);
        }

        return saved;
    }

    // The markings sent since the last time, in order.
    private List<Mark> TakeMarks()
    {
        var marks = new List<Mark>();

        while (_marked.Reader.TryRead(out InvalidateLiquidationsCommand? command))
        {
            marks.Add(new Mark(string.Join('+', command.InstrumentIds.Select(Name).Order()), command.From, command.Until));
        }

        return marks;
    }

    private static string Name(Guid id)
    {
        return id == Btc.Id ? "BTC" : id == Eth.Id ? "ETH" : id == Sol.Id ? "SOL" : id.ToString();
    }

    private static Instrument Delist(Instrument instrument)
    {
        instrument.Delist(Start.AddDays(-1));

        return instrument;
    }

    /// <summary>
    ///     A marking as a test reads it: the contracts by their base assets, as <c>BTC+ETH</c>, and the minutes, both included.
    /// </summary>
    private sealed record Mark(string Contracts, DateTimeOffset From, DateTimeOffset Until);

    /// <summary>
    ///     The stream of Bybit for a test: it records every subscription with the listeners it was made with and takes it, unless the
    ///     test had it refuse the next one.
    /// </summary>
    private sealed class StreamStub : IBybitLiquidationStreamClient
    {
        private readonly Channel<Subscription> _subscriptions = Channel.CreateUnbounded<Subscription>();

        /// <summary>
        ///     Bybit refuses the next subscription, after what the test had its connection do while subscribing.
        /// </summary>
        public bool RefusesNext { get; set; }

        /// <summary>
        ///     The connection of the next subscription is found lost before the subscribing is over.
        /// </summary>
        public bool LosesNextWhileSubscribing { get; set; }

        /// <summary>
        ///     The connection of the next subscription is back, after it was lost, before the subscribing is over.
        /// </summary>
        public bool RestoresNextWhileSubscribing { get; set; }

        public Task<IAsyncDisposable> SubscribeAsync(
            IReadOnlyCollection<string> symbols,
            Action<BybitLinearLiquidation> liquidated,
            Action<IReadOnlyCollection<string>> lost,
            Action<IReadOnlyCollection<string>> restored,
            CancellationToken cancellationToken)
        {
            var subscription = new Subscription([.. symbols], liquidated, lost, restored);

            if (LosesNextWhileSubscribing)
            {
                LosesNextWhileSubscribing = false;
                lost(subscription.Symbols);
            }

            if (RestoresNextWhileSubscribing)
            {
                RestoresNextWhileSubscribing = false;
                restored(subscription.Symbols);
            }

            if (RefusesNext)
            {
                RefusesNext = false;

                return Task.FromException<IAsyncDisposable>(new TransientExchangeException("Bybit did not take the subscription."));
            }

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

    private sealed class Subscription(
        string[] symbols,
        Action<BybitLinearLiquidation> liquidated,
        Action<IReadOnlyCollection<string>> lost,
        Action<IReadOnlyCollection<string>> restored) : IAsyncDisposable
    {
        public string[] Symbols { get; } = symbols;

        public Action<BybitLinearLiquidation> Liquidated { get; } = liquidated;

        public Action<IReadOnlyCollection<string>> Lost { get; } = lost;

        public Action<IReadOnlyCollection<string>> Restored { get; } = restored;

        public bool Ended { get; private set; }

        public ValueTask DisposeAsync()
        {
            Ended = true;

            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task A_connection_found_lost_has_its_contracts_marked_from_the_minute_before_at_every_save_until_it_is_back()
    {
        await StartAsync();
        Subscription subscription = await _stream.NextAsync();
        await PassSavesAsync();
        TakeMarks();

        // Lost at 09:00:35 and back at 09:01:00, after the save of that second.
        subscription.Lost(["BTCUSDT"]);
        await PassSavesAsync(count: 5);
        List<Mark> whileLost = TakeMarks();
        subscription.Restored(["BTCUSDT"]);
        await PassSavesAsync();
        List<Mark> onceBack = TakeMarks();
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                whileLost,
                Is.EqualTo(
                    new[]
                    {
                        new Mark("BTC", Minute.AddMinutes(-1), Minute),
                        new Mark("BTC", Minute, Minute),
                        new Mark("BTC", Minute, Minute),
                        new Mark("BTC", Minute, Minute),
                        new Mark("BTC", Minute, Minute.AddMinutes(1))
                    }));
            Assert.That(onceBack, Is.EqualTo(new[] { new Mark("BTC", Minute.AddMinutes(1), Minute.AddMinutes(1)) }));
            Assert.That(TakeMarks(), Is.Empty);
        });
    }

    [Test]
    public async Task A_connection_found_lost_while_its_contracts_are_subscribed_to_leaves_them_not_heard_until_it_is_back()
    {
        BybitHas(Btc);
        _stream.LosesNextWhileSubscribing = true;
        await StartAsync();
        Subscription subscription = await _stream.NextAsync();

        await PassSavesAsync(count: 2);
        List<Mark> whileLost = TakeMarks();
        subscription.Restored(["BTCUSDT"]);
        await PassSavesAsync();
        List<Mark> onceBack = TakeMarks();
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(whileLost, Is.EqualTo(new[] { new Mark("BTC", Minute, Minute), new Mark("BTC", Minute, Minute) }));
            Assert.That(onceBack, Is.EqualTo(new[] { new Mark("BTC", Minute, Minute) }));
            Assert.That(TakeMarks(), Is.Empty);
        });
    }

    [Test]
    public async Task A_connection_lost_and_back_before_Bybit_refused_the_subscription_leaves_its_contracts_not_heard()
    {
        _last = Minute.AddHours(-3);
        BybitHas(Btc);
        _stream.LosesNextWhileSubscribing = true;
        _stream.RestoresNextWhileSubscribing = true;
        _stream.RefusesNext = true;
        await StartAsync();

        await PassSavesAsync(count: 2);

        Assert.That(
            TakeMarks(),
            Is.EqualTo(new[] { new Mark("BTC", Minute.AddHours(-3), Minute), new Mark("BTC", Minute, Minute), new Mark("BTC", Minute, Minute) }));
    }

    [Test]
    public async Task A_connection_lost_before_Bybit_refused_the_subscription_does_not_keep_its_contracts_not_heard_once_Bybit_takes_them()
    {
        BybitHas(Btc);
        _stream.LosesNextWhileSubscribing = true;
        _stream.RefusesNext = true;
        await StartAsync();

        // The wait for the catalog ends with the 12th save, at 09:01:30; one more save marks what is left.
        await PassSavesAsync(count: 13);
        await _stream.NextAsync();
        TakeMarks();
        await PassSavesAsync();

        Assert.That(TakeMarks(), Is.Empty);
    }

    [Test]
    public async Task A_contract_Bybit_did_not_take_is_marked_at_every_save_and_subscribed_to_after_the_wait()
    {
        BybitHas(Btc);
        _stream.RefusesNext = true;
        await StartAsync();

        await PassSavesAsync();
        List<Mark> atFirst = TakeMarks();
        bool subscribedBeforeTheWait = _stream.HasNext();

        // The wait for the catalog ends with the 12th save, at 09:01:30; one more save marks what is left.
        await PassSavesAsync(count: 12);
        Subscription subscription = await _stream.NextAsync();
        List<Mark> untilTaken = TakeMarks();
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(atFirst, Is.EqualTo(new[] { new Mark("BTC", Minute, Minute) }));
            Assert.That(subscribedBeforeTheWait, Is.False);
            Assert.That(subscription.Symbols, Is.EqualTo(new[] { "BTCUSDT" }));
            Assert.That(untilTaken, Has.Count.GreaterThanOrEqualTo(11));
            Assert.That(untilTaken.Select(mark => mark.Contracts), Is.All.EqualTo("BTC"));
            Assert.That(untilTaken.Max(mark => mark.Until), Is.EqualTo(Minute.AddMinutes(1)));
            Assert.That(TakeMarks(), Is.Empty);
        });
    }

    [Test]
    public async Task A_contract_listed_since_is_subscribed_to_after_the_wait_and_marked_from_when_the_catalog_first_saw_it()
    {
        _last = Minute.AddHours(-3);
        BybitHas(Btc);
        await StartAsync();
        await _stream.NextAsync();
        await PassSavesAsync();
        TakeMarks();

        // The catalog is read again at 09:01:30; one more save marks what is left.
        BybitHas(Btc, Sol);
        await PassSavesAsync(count: 12);
        Subscription subscription = await _stream.NextAsync();

        Assert.Multiple(() =>
        {
            Assert.That(subscription.Symbols, Is.EqualTo(new[] { "SOLUSDT" }));
            Assert.That(TakeMarks(), Is.Not.Empty.And.All.EqualTo(new Mark("SOL", Minute.AddMinutes(1), Minute.AddMinutes(1))));
        });
    }

    [Test]
    public async Task A_marking_that_fails_is_tried_again_at_the_next_save()
    {
        _last = Minute.AddHours(-3);
        _sender.Send(Arg.Any<InvalidateLiquidationsCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Storage is down.")), Marked);
        await StartAsync();

        await PassSavesAsync();
        List<Mark> atFirst = TakeMarks();
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(atFirst, Is.Empty);
            Assert.That(TakeMarks(), Is.EqualTo(new[] { new Mark("BTC+ETH", Minute.AddHours(-3), Minute) }));
        });
    }

    [Test]
    public async Task A_save_that_fails_has_its_minutes_marked_and_its_sums_are_not_saved_again()
    {
        _sender.Send(Arg.Any<SaveLiquidationsCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Storage is down.")), Saved);
        await StartAsync();
        Subscription subscription = await _stream.NextAsync();
        await PassSavesAsync();
        TakeMarks();

        subscription.Liquidated(new BybitLinearLiquidation("BTCUSDT", Minute.AddMinutes(-10).AddSeconds(3), OfLong: true, Size: 2m, Price: 100m));
        await PassSavesAsync();
        List<Mark> marks = TakeMarks();
        subscription.Liquidated(new BybitLinearLiquidation("ETHUSDT", Minute.AddSeconds(41), OfLong: false, Size: 5m, Price: 10m));
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(marks, Is.EqualTo(new[] { new Mark("BTC", Minute.AddMinutes(-10), Minute.AddMinutes(-10)) }));
            Assert.That(
                TakeSaved(),
                Is.EqualTo(
                    new[]
                    {
                        new[] { new LiquidationMinute(Eth.Id, Minute, LiquidationSum.None, new LiquidationSum(volume: 5m, turnover: 50m, count: 1)) }
                    }));
        });
    }

    [Test]
    public async Task A_stream_that_is_off_subscribes_to_nothing_and_marks_nothing()
    {
        await StartAsync(bybit: false);

        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_stream.HasNext(), Is.False);
            Assert.That(TakeMarks(), Is.Empty);
        });
    }

    [Test]
    public async Task A_stream_turned_on_again_marks_from_the_latest_stored_minute_anew()
    {
        await StartAsync();
        Subscription first = await _stream.NextAsync();
        await PassSavesAsync();
        TakeMarks();

        // Off from the pass over the catalog at 09:01:30, on again from the one at 09:02:30; storage holds 09:01 by then.
        Change("LiquidationStream:Bybit:Enabled", "False");
        await PassSavesAsync(count: 11);
        bool ended = first.Ended;
        List<Mark> whileOff = TakeMarks();
        _last = Minute.AddMinutes(1);
        Change("LiquidationStream:Bybit:Enabled", "True");
        await PassSavesAsync(count: 13);
        Subscription second = await _stream.NextAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.True);
            Assert.That(whileOff, Is.Empty);
            Assert.That(second.Symbols, Is.EquivalentTo(new[] { "BTCUSDT", "ETHUSDT" }));
            Assert.That(TakeMarks().First(), Is.EqualTo(new Mark("BTC+ETH", Minute.AddMinutes(1), Minute.AddMinutes(2))));
        });
    }

    [Test]
    public async Task After_a_downtime_the_contracts_are_marked_from_the_latest_stored_minute_to_the_minute_they_are_subscribed_in()
    {
        _last = Minute.AddHours(-3);
        await StartAsync();

        await PassSavesAsync();
        List<Mark> marks = TakeMarks();
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(marks, Is.EqualTo(new[] { new Mark("BTC+ETH", Minute.AddHours(-3), Minute) }));
            Assert.That(TakeMarks(), Is.Empty);
        });
    }

    [Test]
    public async Task Liquidations_are_summed_by_contract_minute_and_side_and_saved_once_at_the_next_save()
    {
        await StartAsync();
        Subscription subscription = await _stream.NextAsync();

        subscription.Liquidated(new BybitLinearLiquidation("BTCUSDT", Minute.AddSeconds(31), OfLong: true, Size: 2m, Price: 100m));
        subscription.Liquidated(new BybitLinearLiquidation("BTCUSDT", Minute.AddSeconds(32), OfLong: true, Size: 1m, Price: 110m));
        subscription.Liquidated(new BybitLinearLiquidation("BTCUSDT", Minute.AddSeconds(33), OfLong: false, Size: 3m, Price: 100m));
        subscription.Liquidated(new BybitLinearLiquidation("BTCUSDT", Minute.AddSeconds(-5), OfLong: true, Size: 1m, Price: 100m));
        subscription.Liquidated(new BybitLinearLiquidation("ETHUSDT", Minute.AddSeconds(34), OfLong: false, Size: 5m, Price: 10m));
        await PassSavesAsync();
        List<IReadOnlyCollection<LiquidationMinute>> saved = TakeSaved();
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved, Has.Count.EqualTo(1));
            Assert.That(
                saved.SelectMany(minutes => minutes),
                Is.EquivalentTo(
                    new[]
                    {
                        new LiquidationMinute(
                            Btc.Id,
                            Minute,
                            new LiquidationSum(volume: 3m, turnover: 310m, count: 2),
                            new LiquidationSum(volume: 3m, turnover: 300m, count: 1)),
                        new LiquidationMinute(Btc.Id, Minute.AddMinutes(-1), new LiquidationSum(volume: 1m, turnover: 100m, count: 1), LiquidationSum.None),
                        new LiquidationMinute(Eth.Id, Minute, LiquidationSum.None, new LiquidationSum(volume: 5m, turnover: 50m, count: 1))
                    }));
            Assert.That(TakeSaved(), Is.Empty);
        });
    }

    [Test]
    public async Task Nothing_is_subscribed_to_until_the_latest_stored_minute_is_read()
    {
        _last = Minute.AddHours(-3);
        _sender.Send(new GetLastLiquidationTimeQuery(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<DateTimeOffset?>(new InvalidOperationException("Storage is down.")), _ => Task.FromResult(_last));
        await StartAsync();

        await PassSavesAsync();
        bool subscribedAtFirst = _stream.HasNext();
        List<Mark> atFirst = TakeMarks();

        // The wait for the catalog ends with the 12th save, at 09:01:30.
        await PassSavesAsync(count: 11);

        Assert.Multiple(() =>
        {
            Assert.That(subscribedAtFirst, Is.False);
            Assert.That(atFirst, Is.Empty);
            Assert.That(_stream.HasNext(), Is.True);
            Assert.That(TakeMarks().First().From, Is.EqualTo(Minute.AddHours(-3)));
        });
    }

    [Test]
    public async Task Only_the_listed_linear_contracts_are_subscribed_to_all_in_one_subscription()
    {
        await StartAsync();

        Subscription subscription = await _stream.NextAsync();

        Assert.Multiple(() =>
        {
            Assert.That(subscription.Symbols, Is.EquivalentTo(new[] { "BTCUSDT", "ETHUSDT" }));
            Assert.That(_stream.HasNext(), Is.False);
        });
    }

    [Test]
    public async Task The_first_listening_marks_only_the_minute_it_starts_in()
    {
        await StartAsync();

        await PassSavesAsync();
        List<Mark> marks = TakeMarks();
        await PassSavesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(marks, Is.EqualTo(new[] { new Mark("BTC+ETH", Minute, Minute) }));
            Assert.That(TakeMarks(), Is.Empty);
        });
    }

    [Test]
    public async Task The_subscription_ends_when_the_service_stops()
    {
        await StartAsync();
        Subscription subscription = await _stream.NextAsync();

        await _service!.StopAsync(CancellationToken.None);

        Assert.That(subscription.Ended, Is.True);
    }
}