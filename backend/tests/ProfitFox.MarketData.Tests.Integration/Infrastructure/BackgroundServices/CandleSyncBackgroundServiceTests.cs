using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Integration.Time;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class CandleSyncBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _sent = Channel.CreateUnbounded<LoadCandlesCommand>();
        _queue = new CandleLoadQueue();
        _time = new WatchedTimeProvider(Start);
        _sender = Substitute.For<ISender>();
        _sender.Send(new GetInstrumentsQuery(Exchange.Bybit), Arg.Any<CancellationToken>()).Returns([Btc.ToInternal(), Delisted.ToInternal()]);
        _sender.Send(new GetInstrumentsQuery(Exchange.TInvest), Arg.Any<CancellationToken>()).Returns([Sber.ToInternal()]);

        // Every command is recorded; the first page of an instrument leaves more to load, the next ones nothing.
        var loaded = new HashSet<Guid>();
        _sender.Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is not LoadCandlesCommand command)
                {
                    return Task.FromResult(LoadCandlesResult.NothingLeft);
                }

                _sent.Writer.TryWrite(command);

                lock (loaded)
                {
                    return Task.FromResult(loaded.Add(command.InstrumentId) ? LoadCandlesResult.MoreLeft : LoadCandlesResult.NothingLeft);
                }
            });
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

    private static readonly DateTimeOffset StartMinute = Start.AddSeconds(-30);

    private static readonly MinuteRange History = new MinuteRange(StartMinute.AddDays(-365), StartMinute);

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private static readonly Instrument Delisted = Delist(InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA")));

    private static readonly Instrument Sber = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());

    private Channel<LoadCandlesCommand> _sent = null!;
    private CandleLoadQueue _queue = null!;
    private WatchedTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider? _services;
    private CandleSyncBackgroundService? _service;

    // The settings as the app binds them; the sender is the last ISender registered, so it wins over the one of MediatR.
    private async Task StartAsync(bool bybit = true, bool tinvest = true)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["CandleSync:HistoryDays"] = "365",
                    ["CandleSync:IdleSeconds"] = "60",
                    ["CandleSync:Bybit:Enabled"] = bybit.ToString(),
                    ["CandleSync:TInvest:Enabled"] = tinvest.ToString(),
                    ["TInvestApi:Token"] = "test-token"
                })
            .Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new CandleSyncBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _queue,
            _services.GetRequiredService<IOptionsMonitor<CandleSyncSettings>>(),
            _time,
            NullLogger<CandleSyncBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<LoadCandlesCommand>> SentAsync(int count)
    {
        var sent = new List<LoadCandlesCommand>();

        while (sent.Count < count)
        {
            sent.Add(await _sent.Reader.ReadAsync().AsTask().WaitAsync(Patience));
        }

        return sent;
    }

    // The waits the two exchanges are in once both stopped loading, in the order they started them.
    private async Task<IReadOnlyList<TimeSpan>> BothExchangesWaitAsync()
    {
        return [await _time.NextWaitAsync(), await _time.NextWaitAsync()];
    }

    private void BybitHas(params Instrument[] instruments)
    {
        _sender.Send(new GetInstrumentsQuery(Exchange.Bybit), Arg.Any<CancellationToken>())
            .Returns([.. instruments.Select(instrument => instrument.ToInternal())]);
    }

    private static LoadCandlesCommand PageOf(Instrument instrument)
    {
        return Arg.Is<LoadCandlesCommand>(command => command != null && command.InstrumentId == instrument.Id);
    }

    private static Instrument Delist(Instrument instrument)
    {
        instrument.Delist(Start.AddDays(-1));

        return instrument;
    }

    [Test]
    public async Task A_hung_exchange_does_not_hold_back_the_other()
    {
        _sender.Send(PageOf(Btc), Arg.Any<CancellationToken>())
            .Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()).ContinueWith(_ => LoadCandlesResult.NothingLeft));

        await StartAsync();

        Assert.That((await SentAsync(count: 2)).Select(command => command.InstrumentId), Is.All.EqualTo(Sber.Id));
    }

    [Test]
    public async Task A_pause_of_the_exchange_holds_its_loading_until_the_pause_ends()
    {
        _sender.Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<LoadCandlesResult>(new ExchangePausedException("Bybit limited the requests.", Start.AddSeconds(30))),
                call =>
                {
                    if (call[0] is LoadCandlesCommand command)
                    {
                        _sent.Writer.TryWrite(command);
                    }

                    return Task.FromResult(LoadCandlesResult.NothingLeft);
                });

        await StartAsync(tinvest: false);
        IReadOnlyList<TimeSpan> waits = await BothExchangesWaitAsync();
        bool sentInPause = _sent.Reader.TryRead(out _);
        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Multiple(async () =>
        {
            Assert.That(waits, Is.EquivalentTo(new[] { TimeSpan.FromSeconds(30), Idle }));
            Assert.That(sentInPause, Is.False);
            Assert.That((await SentAsync(count: 1)).Single().RequestedAt, Is.EqualTo(Start.AddSeconds(30)));
        });
    }

    [Test]
    public async Task A_range_a_read_asked_for_loads_before_the_history_and_leaves_the_queue_once_loaded()
    {
        var range = new MinuteRange(StartMinute.AddDays(-800), StartMinute.AddDays(-799));
        _queue.Enqueue(Delisted.Id, range);

        await StartAsync(tinvest: false);
        IReadOnlyList<LoadCandlesCommand> firstRound = await SentAsync(count: 2);
        await BothExchangesWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstRound, Is.EqualTo(new[] { new LoadCandlesCommand(Delisted.Id, range, Start), new LoadCandlesCommand(Btc.Id, History, Start) }));
            Assert.That(_queue.Pending(), Is.Empty);
        });
    }

    [Test]
    public async Task A_range_whose_page_fails_leaves_the_queue()
    {
        _queue.Enqueue(Delisted.Id, new MinuteRange(StartMinute.AddDays(-800), StartMinute.AddDays(-799)));
        _sender.Send(PageOf(Delisted), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The page is broken."));

        await StartAsync(tinvest: false);
        await BothExchangesWaitAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(_queue.Pending(), Is.Empty);
            await _sender.Received(1).Send(PageOf(Delisted), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task A_round_follows_at_once_while_pages_are_left_and_the_exchange_idles_once_none_are()
    {
        await StartAsync(tinvest: false);
        await SentAsync(count: 2);
        await BothExchangesWaitAsync();

        _time.Advance(Idle - TimeSpan.FromSeconds(1));
        bool sentEarly = _sent.Reader.TryRead(out _);
        _time.Advance(TimeSpan.FromSeconds(1));
        IReadOnlyList<LoadCandlesCommand> afterIdle = await SentAsync(count: 1);

        Assert.Multiple(() =>
        {
            Assert.That(sentEarly, Is.False);
            Assert.That(afterIdle.Single().RequestedAt, Is.EqualTo(Start + Idle));
        });
    }

    [Test]
    public async Task An_exchange_that_is_not_enabled_loads_nothing()
    {
        await StartAsync(bybit: false);

        IReadOnlyList<LoadCandlesCommand> sent = await SentAsync(count: 2);

        Assert.That(sent.Select(command => command.InstrumentId), Is.All.EqualTo(Sber.Id));
    }

    [Test]
    public async Task An_exchange_that_is_unavailable_stops_the_round_and_is_tried_again_after_the_idle_wait()
    {
        BybitHas(Btc, Eth);
        _sender.Send(PageOf(Btc), Arg.Any<CancellationToken>()).ThrowsAsync(new TransientExchangeException("Bybit answered 502."));

        await StartAsync(tinvest: false);
        await BothExchangesWaitAsync();
        bool sentAfterTheFailure = _sent.Reader.TryRead(out _);
        _time.Advance(Idle);
        await BothExchangesWaitAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(sentAfterTheFailure, Is.False);
            await _sender.Received(2).Send(PageOf(Btc), Arg.Any<CancellationToken>());
            await _sender.DidNotReceive().Send(PageOf(Eth), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task An_instrument_that_fails_does_not_stop_the_others()
    {
        BybitHas(Btc, Eth);
        _sender.Send(PageOf(Btc), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("Bybit refused the symbol."));

        await StartAsync(tinvest: false);

        Assert.That((await SentAsync(count: 1)).Single().InstrumentId, Is.EqualTo(Eth.Id));
    }

    [Test]
    public async Task Every_listed_instrument_loads_a_page_of_its_history_when_the_app_starts()
    {
        await StartAsync();

        // The exchanges load at the same time, so the first commands of the two come in either order, and Bybit may already be in its
        // next round.
        var first = new Dictionary<Guid, LoadCandlesCommand>();

        while (first.Count < 2)
        {
            LoadCandlesCommand command = (await SentAsync(count: 1)).Single();
            first.TryAdd(command.InstrumentId, command);
        }

        Assert.That(
            first.Values,
            Is.EquivalentTo(new[] { new LoadCandlesCommand(Btc.Id, History, Start), new LoadCandlesCommand(Sber.Id, History, Start) }));
    }
}