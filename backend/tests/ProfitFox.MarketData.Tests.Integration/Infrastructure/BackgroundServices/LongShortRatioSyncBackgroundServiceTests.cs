using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.Core;

using ProfitFox.MarketData.Application.Commands.LongShortRatio.LoadLongShortRatio;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Integration.Time;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class LongShortRatioSyncBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _sent = Channel.CreateUnbounded<LoadLongShortRatioCommand>();
        _time = new WatchedTimeProvider(Start);
        _sender = Substitute.For<ISender>();

        // Of the catalog the listed linear contracts quoted in USDT have a long/short ratio, the dated one too.
        _sender.Send(new GetInstrumentsQuery(Exchange.Bybit), Arg.Any<CancellationToken>())
            .Returns([Btc.ToInternal(), Spot.ToInternal(), Delisted.ToInternal(), Usdc.ToInternal(), Futures.ToInternal()]);
        _sender.Send(Arg.Any<LoadLongShortRatioCommand>(), Arg.Any<CancellationToken>()).Returns(NothingLeft);
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

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 8, second: 0, TimeSpan.Zero);

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(3600);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly DateTimeOffset BtcLaunch = ((BybitListedInstrument)Btc.Listing).LaunchTime!.Value;

    private static readonly Instrument Futures = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateFutures());

    private static readonly Instrument Spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());

    private static readonly Instrument Usdc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateUsdcPerpetual());

    private static readonly Instrument Delisted = Delist(InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA")));

    private Channel<LoadLongShortRatioCommand> _sent = null!;
    private WatchedTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider? _services;
    private LongShortRatioSyncBackgroundService? _service;

    // The settings as the app binds them, one contract at a time, so the pages come in the order of the catalog; the sender is the last
    // ISender registered, so it wins over the one of MediatR.
    private async Task StartAsync(bool bybit = true, string[]? fullHistory = null)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["LongShortRatioSync:HistoryDays"] = "365",
                    ["LongShortRatioSync:IdleSeconds"] = "3600",
                    ["LongShortRatioSync:Parallelism"] = "1",
                    ["LongShortRatioSync:Bybit:Enabled"] = bybit.ToString()
                })
            .AddInMemoryCollection(FullHistoryConfiguration.Of(fullHistory ?? []))
            .Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new LongShortRatioSyncBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IOptionsMonitor<LongShortRatioSyncSettings>>(),
            _services.GetRequiredService<IOptionsMonitor<FullHistorySettings>>(),
            _time,
            NullLogger<LongShortRatioSyncBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
    }

    // A page that is loaded and leaves nothing: the command is recorded.
    private Task<LoadLongShortRatioResult> NothingLeft(CallInfo call)
    {
        if (call[0] is LoadLongShortRatioCommand command)
        {
            _sent.Writer.TryWrite(command);
        }

        return Task.FromResult(LoadLongShortRatioResult.NothingLeft);
    }

    private async Task<IReadOnlyList<LoadLongShortRatioCommand>> SentAsync(int count)
    {
        var sent = new List<LoadLongShortRatioCommand>();

        while (sent.Count < count)
        {
            sent.Add(await _sent.Reader.ReadAsync().AsTask().WaitAsync(Patience));
        }

        return sent;
    }

    private static LoadLongShortRatioCommand PageOf(Instrument instrument, DateTimeOffset requestedAt)
    {
        return new LoadLongShortRatioCommand(instrument.Id, requestedAt.AddDays(-365), requestedAt);
    }

    private static Instrument Delist(Instrument instrument)
    {
        instrument.Delist(Start.AddDays(-1));

        return instrument;
    }

    [Test]
    public async Task A_contract_named_for_a_whole_history_loads_it_from_its_launch()
    {
        await StartAsync(fullHistory: ["BTCUSDT"]);

        IReadOnlyList<LoadLongShortRatioCommand> sent = await SentAsync(count: 2);

        // The dated contract of BTC has a symbol of its own, so it keeps a year.
        Assert.That(sent, Is.EqualTo(new[] { new LoadLongShortRatioCommand(Btc.Id, BtcLaunch, Start), PageOf(Futures, Start) }));
    }

    [Test]
    public async Task A_contract_whose_page_failed_is_left_until_after_the_idle_wait()
    {
        // The page of BTC fails; the dated contract has history for two rounds.
        var loaded = 0;
        _sender.Send(Arg.Any<LoadLongShortRatioCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is not LoadLongShortRatioCommand command)
                {
                    return Task.FromResult(LoadLongShortRatioResult.NothingLeft);
                }

                _sent.Writer.TryWrite(command);

                if (command.InstrumentId == Btc.Id)
                {
                    return Task.FromException<LoadLongShortRatioResult>(new InvalidOperationException("The database is away."));
                }

                return Task.FromResult(Interlocked.Increment(ref loaded) == 1 ? LoadLongShortRatioResult.MoreLeft : LoadLongShortRatioResult.NothingLeft);
            });

        await StartAsync();
        IReadOnlyList<LoadLongShortRatioCommand> beforeTheWait = await SentAsync(count: 3);
        TimeSpan wait = await _time.NextWaitAsync();
        _time.Advance(wait);
        IReadOnlyList<LoadLongShortRatioCommand> afterTheWait = await SentAsync(count: 2);

        Assert.Multiple(() =>
        {
            // The second round asks only the contract that did not fail; the round after the wait asks both again.
            Assert.That(beforeTheWait.Select(command => command.InstrumentId), Is.EqualTo(new[] { Btc.Id, Futures.Id, Futures.Id }));
            Assert.That(wait, Is.EqualTo(Idle));
            Assert.That(afterTheWait.Select(command => command.InstrumentId), Is.EqualTo(new[] { Btc.Id, Futures.Id }));
        });
    }

    [Test]
    public async Task A_round_that_leaves_nothing_is_followed_by_the_idle_wait_and_a_round_as_of_then()
    {
        await StartAsync();
        await SentAsync(count: 2);
        TimeSpan wait = await _time.NextWaitAsync();
        _time.Advance(wait);

        IReadOnlyList<LoadLongShortRatioCommand> sent = await SentAsync(count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(wait, Is.EqualTo(Idle));

            // The round after the wait looks for the steps told meanwhile.
            Assert.That(sent, Is.EqualTo(new[] { PageOf(Btc, Start + Idle), PageOf(Futures, Start + Idle) }));
        });
    }

    [Test]
    public async Task Bybit_that_is_not_enabled_is_not_asked()
    {
        await StartAsync(bybit: false);
        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(wait, Is.EqualTo(Idle));
            await _sender.DidNotReceive().Send(Arg.Any<LoadLongShortRatioCommand>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Bybit_that_is_unavailable_stops_the_round_until_after_the_idle_wait()
    {
        _sender.Send(Arg.Any<LoadLongShortRatioCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<LoadLongShortRatioResult>(new TransientExchangeException("Bybit answered 502.")), NothingLeft);

        await StartAsync();
        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(async () =>
        {
            // The other contract would fail the same way after retries of its own.
            Assert.That(wait, Is.EqualTo(Idle));
            await _sender.Received(1).Send(Arg.Any<LoadLongShortRatioCommand>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Rounds_follow_one_another_without_a_wait_while_pages_are_left()
    {
        // The first page of a contract leaves more to load, the next ones nothing.
        var loaded = new HashSet<Guid>();
        _sender.Send(Arg.Any<LoadLongShortRatioCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is not LoadLongShortRatioCommand command)
                {
                    return Task.FromResult(LoadLongShortRatioResult.NothingLeft);
                }

                _sent.Writer.TryWrite(command);

                return Task.FromResult(loaded.Add(command.InstrumentId) ? LoadLongShortRatioResult.MoreLeft : LoadLongShortRatioResult.NothingLeft);
            });

        await StartAsync();
        IReadOnlyList<LoadLongShortRatioCommand> sent = await SentAsync(count: 4);
        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sent.Select(command => command.InstrumentId), Is.EqualTo(new[] { Btc.Id, Futures.Id, Btc.Id, Futures.Id }));
            Assert.That(wait, Is.EqualTo(Idle));
        });
    }

    [Test]
    public async Task The_start_loads_one_page_of_every_listed_linear_contract_quoted_in_USDT_a_year_back()
    {
        await StartAsync();

        IReadOnlyList<LoadLongShortRatioCommand> sent = await SentAsync(count: 2);
        await _time.NextWaitAsync();

        Assert.Multiple(async () =>
        {
            // A spot pair, a delisted contract and a contract quoted in USDC are not asked for; a dated futures contract is.
            Assert.That(sent, Is.EqualTo(new[] { PageOf(Btc, Start), PageOf(Futures, Start) }));
            await _sender.Received(2).Send(Arg.Any<LoadLongShortRatioCommand>(), Arg.Any<CancellationToken>());
        });
    }
}