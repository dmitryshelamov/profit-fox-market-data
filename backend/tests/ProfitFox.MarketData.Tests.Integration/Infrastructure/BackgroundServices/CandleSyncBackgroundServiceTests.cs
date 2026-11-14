using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Infrastructure.Exchanges;
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
        _time = new FakeTimeProvider(Start);
        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<GetBybitInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns([Btc.ToInternal(), Delisted.ToInternal()]);
        _sender.Send(Arg.Any<GetTInvestSharesQuery>(), Arg.Any<CancellationToken>()).Returns([Sber.ToInternal()]);

        // A page left the first time an instrument loads, none after; every command is recorded.
        var loaded = new HashSet<Guid>();
        _sender.Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is not LoadCandlesCommand command)
                {
                    return Task.FromResult(false);
                }

                _sent.Writer.TryWrite(command);

                lock (loaded)
                {
                    return Task.FromResult(loaded.Add(command.InstrumentId));
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

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Delisted = Delist(InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA")));

    private static readonly Instrument Sber = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());

    private Channel<LoadCandlesCommand> _sent = null!;
    private CandleLoadQueue _queue = null!;
    private FakeTimeProvider _time = null!;
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
            sent.Add(await _sent.Reader.ReadAsync().AsTask().WaitAsync(Wait));
        }

        return sent;
    }

    private static Instrument Delist(Instrument instrument)
    {
        instrument.Delist(Start.AddDays(-1));

        return instrument;
    }

    [Test]
    public async Task A_hung_exchange_does_not_hold_back_the_other()
    {
        _sender.Send(Arg.Is<LoadCandlesCommand>(command => command!.InstrumentId == Btc.Id), Arg.Any<CancellationToken>())
            .Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()).ContinueWith(_ => false));

        await StartAsync();

        Assert.That((await SentAsync(count: 2)).Select(command => command.InstrumentId), Does.Contain(Sber.Id));
    }

    [Test]
    public async Task A_pass_follows_at_once_while_pages_are_left_and_waits_once_none_are()
    {
        await StartAsync(tinvest: false);
        await SentAsync(count: 2);

        // A moment of real time for the service to reach its wait after the second pass.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        _time.Advance(TimeSpan.FromSeconds(59));
        bool early = _sent.Reader.TryRead(out _);
        _time.Advance(TimeSpan.FromSeconds(1));
        IReadOnlyList<LoadCandlesCommand> afterIdle = await SentAsync(count: 1);

        Assert.Multiple(() =>
        {
            Assert.That(early, Is.False);
            Assert.That(afterIdle.Single().RequestedAt, Is.EqualTo(Start.AddMinutes(1)));
        });
    }

    [Test]
    public async Task A_pause_of_the_exchange_holds_its_loading_until_the_pause_ends()
    {
        _sender.Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<bool>(new ExchangePausedException("Bybit limited the requests.", Start.AddSeconds(30))),
                call =>
                {
                    _sent.Writer.TryWrite(call.Arg<LoadCandlesCommand>()!);

                    return Task.FromResult(false);
                });

        await StartAsync(tinvest: false);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        bool sentInPause = _sent.Reader.TryRead(out _);
        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Multiple(async () =>
        {
            Assert.That(sentInPause, Is.False);
            Assert.That((await SentAsync(count: 1)).Single().RequestedAt, Is.EqualTo(Start.AddSeconds(30)));
        });
    }

    [Test]
    public async Task A_window_a_read_asked_for_loads_before_the_history_and_leaves_the_queue_once_loaded()
    {
        var window = new MinuteRange(StartMinute.AddDays(-800), StartMinute.AddDays(-799));
        var request = new CandleLoadRequest(Delisted.Id, window);
        _queue.Enqueue(request);

        await StartAsync(tinvest: false);
        IReadOnlyList<LoadCandlesCommand> sent = await SentAsync(count: 2);

        // The second round: the window, which has nothing left now and leaves the queue, then the history.
        await SentAsync(count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.EqualTo(new[] { new LoadCandlesCommand(Delisted.Id, window, Start), sent[1] }));
            Assert.That(sent[1].InstrumentId, Is.EqualTo(Btc.Id));
            Assert.That(_queue.Pending(), Is.Empty);
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
    public async Task An_instrument_that_fails_does_not_stop_the_others()
    {
        Instrument eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));
        _sender.Send(Arg.Any<GetBybitInstrumentsQuery>(), Arg.Any<CancellationToken>()).Returns([Btc.ToInternal(), eth.ToInternal()]);
        _sender.Send(Arg.Is<LoadCandlesCommand>(command => command!.InstrumentId == Btc.Id), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Bybit is down."));

        await StartAsync(tinvest: false);

        Assert.That((await SentAsync(count: 1)).Single().InstrumentId, Is.EqualTo(eth.Id));
    }

    [Test]
    public async Task Every_listed_instrument_loads_a_page_of_its_history_when_the_app_starts()
    {
        await StartAsync();

        // The exchanges load at the same time, so the first commands of the two come in either order, and Bybit may already be in its
        // next pass.
        var first = new Dictionary<Guid, LoadCandlesCommand>();

        while (first.Count < 2)
        {
            LoadCandlesCommand command = (await SentAsync(count: 1)).Single();
            first.TryAdd(command.InstrumentId, command);
        }

        Assert.That(
            first.Values,
            Is.EquivalentTo(
                new[]
                {
                    new LoadCandlesCommand(Btc.Id, new MinuteRange(StartMinute.AddDays(-365), StartMinute), Start),
                    new LoadCandlesCommand(Sber.Id, new MinuteRange(StartMinute.AddDays(-365), StartMinute), Start)
                }));
    }
}