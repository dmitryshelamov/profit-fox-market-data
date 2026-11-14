using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Candles.LoadBuyVolumes;
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
public class BuyVolumeSyncBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _sent = Channel.CreateUnbounded<LoadBuyVolumesCommand>();
        _time = new WatchedTimeProvider(Start);
        _sender = Substitute.For<ISender>();
        _sender.Send(new GetInstrumentsQuery(Exchange.Bybit), Arg.Any<CancellationToken>())
            .Returns([Btc.ToInternal(), Eth.ToInternal(), Delisted.ToInternal()]);

        // Every command is recorded; the first one of an instrument leaves more days, the next ones nothing.
        var asked = new HashSet<Guid>();
        _sender.Send(Arg.Any<LoadBuyVolumesCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is not LoadBuyVolumesCommand command)
                {
                    return Task.FromResult(LoadBuyVolumesResult.NothingLeft);
                }

                _sent.Writer.TryWrite(command);

                lock (asked)
                {
                    return Task.FromResult(asked.Add(command.InstrumentId) ? LoadBuyVolumesResult.MoreLeft : LoadBuyVolumesResult.NothingLeft);
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

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(600);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private static readonly Instrument Delisted = Delist(InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA")));

    private Channel<LoadBuyVolumesCommand> _sent = null!;
    private WatchedTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider? _services;
    private BuyVolumeSyncBackgroundService? _service;

    // The settings as the app binds them, one instrument at a time so the order is known; the sender is the last ISender registered, so
    // it wins over the one of MediatR.
    private async Task StartAsync(bool bybit = true)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["BuyVolumeSync:HistoryDays"] = "365",
                    ["BuyVolumeSync:IdleSeconds"] = "600",
                    ["BuyVolumeSync:Parallelism"] = "1",
                    ["BuyVolumeSync:Bybit:Enabled"] = bybit.ToString()
                })
            .Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new BuyVolumeSyncBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IOptionsMonitor<BuyVolumeSyncSettings>>(),
            _time,
            NullLogger<BuyVolumeSyncBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<LoadBuyVolumesCommand>> SentAsync(int count)
    {
        var sent = new List<LoadBuyVolumesCommand>();

        while (sent.Count < count)
        {
            sent.Add(await _sent.Reader.ReadAsync().AsTask().WaitAsync(Patience));
        }

        return sent;
    }

    private static LoadBuyVolumesCommand CommandOf(Instrument instrument)
    {
        return Arg.Is<LoadBuyVolumesCommand>(command => command != null && command.InstrumentId == instrument.Id);
    }

    private static Instrument Delist(Instrument instrument)
    {
        instrument.Delist(Start.AddDays(-1));

        return instrument;
    }

    [Test]
    public async Task A_round_asks_for_the_history_of_every_listed_instrument_and_rounds_follow_while_days_are_left()
    {
        await StartAsync();

        IReadOnlyList<LoadBuyVolumesCommand> sent = await SentAsync(count: 4);
        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                sent,
                Is.EqualTo(
                    new[]
                    {
                        new LoadBuyVolumesCommand(Btc.Id, History, Start),
                        new LoadBuyVolumesCommand(Eth.Id, History, Start),
                        new LoadBuyVolumesCommand(Btc.Id, History, Start),
                        new LoadBuyVolumesCommand(Eth.Id, History, Start)
                    }));
            Assert.That(wait, Is.EqualTo(Idle));
            Assert.That(_sent.Reader.TryPeek(out _), Is.False);
        });
    }

    [Test]
    public async Task A_sync_that_is_off_asks_for_nothing()
    {
        await StartAsync(bybit: false);

        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(wait, Is.EqualTo(Idle));
            Assert.That(_sent.Reader.TryPeek(out _), Is.False);
        });
    }

    [Test]
    public async Task An_archive_that_is_unavailable_stops_the_round_until_after_the_wait()
    {
        _sender.Send(CommandOf(Btc), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<LoadBuyVolumesResult>(new TransientExchangeException("The archive cannot be read.")));

        await StartAsync();
        TimeSpan wait = await _time.NextWaitAsync();
        bool askedForOthers = _sent.Reader.TryPeek(out _);
        _time.Advance(wait);

        Assert.Multiple(async () =>
        {
            Assert.That(wait, Is.EqualTo(Idle));
            Assert.That(askedForOthers, Is.False);
            await _time.NextWaitAsync();
            await _sender.Received(2).Send(CommandOf(Btc), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task An_instrument_that_fails_does_not_stop_the_others()
    {
        _sender.Send(CommandOf(Btc), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<LoadBuyVolumesResult>(new InvalidOperationException("Storage is down.")));

        await StartAsync();

        Assert.That((await SentAsync(count: 2)).Select(command => command.InstrumentId), Is.All.EqualTo(Eth.Id));
    }
}