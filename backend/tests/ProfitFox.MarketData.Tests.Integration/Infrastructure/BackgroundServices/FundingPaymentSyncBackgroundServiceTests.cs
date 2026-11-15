using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Integration.Time;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class FundingPaymentSyncBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _sent = Channel.CreateUnbounded<LoadFundingPaymentsCommand>();
        _sender = Substitute.For<ISender>();

        // Of the catalog only the two listed perpetual contracts pay funding.
        _sender.Send(new GetInstrumentsQuery(Exchange.Bybit), Arg.Any<CancellationToken>())
            .Returns([Btc.ToInternal(), Futures.ToInternal(), Spot.ToInternal(), Delisted.ToInternal(), Eth.ToInternal()]);
        _sender.Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>()).Returns(NothingLeft);
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

    // Ten minutes past a whole hour.
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 10, second: 0, TimeSpan.Zero);

    // From the start to half a minute after the next whole hour.
    private static readonly TimeSpan ToNextLoad = TimeSpan.FromMinutes(50) + TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private static readonly Instrument Futures = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateFutures());

    private static readonly Instrument Spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());

    private static readonly Instrument Delisted = Delist(InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA")));

    private Channel<LoadFundingPaymentsCommand> _sent = null!;
    private WatchedTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider? _services;
    private FundingPaymentSyncBackgroundService? _service;

    // The settings as the app binds them, one contract at a time, so the pages come in the order of the catalog; the sender is the last
    // ISender registered, so it wins over the one of MediatR.
    private async Task StartAsync(bool bybit = true, DateTimeOffset? at = null)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["FundingPaymentSync:HistoryDays"] = "365",
                    ["FundingPaymentSync:Parallelism"] = "1",
                    ["FundingPaymentSync:Bybit:Enabled"] = bybit.ToString()
                })
            .Build();
        _time = new WatchedTimeProvider(at ?? Start);
        _services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new FundingPaymentSyncBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IOptionsMonitor<FundingPaymentSyncSettings>>(),
            _time,
            NullLogger<FundingPaymentSyncBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
    }

    // A page that is loaded and leaves nothing: the command is recorded.
    private Task<LoadFundingPaymentsResult> NothingLeft(NSubstitute.Core.CallInfo call)
    {
        if (call[0] is LoadFundingPaymentsCommand command)
        {
            _sent.Writer.TryWrite(command);
        }

        return Task.FromResult(LoadFundingPaymentsResult.NothingLeft);
    }

    private async Task<IReadOnlyList<LoadFundingPaymentsCommand>> SentAsync(int count)
    {
        var sent = new List<LoadFundingPaymentsCommand>();

        while (sent.Count < count)
        {
            sent.Add(await _sent.Reader.ReadAsync().AsTask().WaitAsync(Patience));
        }

        return sent;
    }

    private static LoadFundingPaymentsCommand PageOf(Instrument instrument, DateTimeOffset requestedAt)
    {
        return new LoadFundingPaymentsCommand(instrument.Id, requestedAt.AddDays(-365), requestedAt);
    }

    private static Instrument Delist(Instrument instrument)
    {
        instrument.Delist(Start.AddDays(-1));

        return instrument;
    }

    [Test]
    public async Task A_contract_whose_page_failed_is_left_until_the_next_load()
    {
        // The page of BTC fails; ETH has history for two rounds.
        var loaded = 0;
        _sender.Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is not LoadFundingPaymentsCommand command)
                {
                    return Task.FromResult(LoadFundingPaymentsResult.NothingLeft);
                }

                _sent.Writer.TryWrite(command);

                if (command.InstrumentId == Btc.Id)
                {
                    return Task.FromException<LoadFundingPaymentsResult>(new InvalidOperationException("The database is away."));
                }

                return Task.FromResult(Interlocked.Increment(ref loaded) == 1 ? LoadFundingPaymentsResult.MoreLeft : LoadFundingPaymentsResult.NothingLeft);
            });

        await StartAsync();
        IReadOnlyList<LoadFundingPaymentsCommand> firstLoad = await SentAsync(count: 3);
        TimeSpan wait = await _time.NextWaitAsync();
        _time.Advance(wait);
        IReadOnlyList<LoadFundingPaymentsCommand> secondLoad = await SentAsync(count: 2);

        Assert.Multiple(() =>
        {
            // The second round of the load asks only the contract that did not fail; the load after the hour asks both again.
            Assert.That(firstLoad.Select(command => command.InstrumentId), Is.EqualTo(new[] { Btc.Id, Eth.Id, Eth.Id }));
            Assert.That(wait, Is.EqualTo(ToNextLoad));
            Assert.That(secondLoad.Select(command => command.InstrumentId), Is.EqualTo(new[] { Btc.Id, Eth.Id }));
        });
    }

    [Test]
    public async Task A_load_still_going_at_a_whole_hour_is_followed_by_the_load_after_the_hour_that_comes_next()
    {
        // The first page takes 55 minutes of the clock: the load ends at 13:05, after the moment of 13:00:30 has passed.
        var slow = 1;
        _sender.Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (Interlocked.Exchange(ref slow, value: 0) == 1)
                {
                    _time.Advance(TimeSpan.FromMinutes(55));
                }

                return NothingLeft(call);
            });

        await StartAsync();
        await SentAsync(count: 2);
        TimeSpan wait = await _time.NextWaitAsync();
        _time.Advance(wait);
        IReadOnlyList<LoadFundingPaymentsCommand> sent = await SentAsync(count: 2);
        DateTimeOffset afterTheNextHour = Start.AddMinutes(110).AddSeconds(30);

        Assert.Multiple(() =>
        {
            // The load was not cut short at 13:00:30, and the next one takes what was paid meanwhile: it asks as of 14:00:30.
            Assert.That(wait, Is.EqualTo(TimeSpan.FromMinutes(55) + TimeSpan.FromSeconds(30)));
            Assert.That(sent, Is.EqualTo(new[] { PageOf(Btc, afterTheNextHour), PageOf(Eth, afterTheNextHour) }));
        });
    }

    [Test]
    public async Task A_load_that_ends_within_half_a_minute_after_a_whole_hour_is_followed_by_the_load_of_that_hour()
    {
        // Ten seconds past the hour: its payments may not be told yet, so the load of this hour is still to come.
        await StartAsync(at: Start.AddMinutes(-10).AddSeconds(10));
        await SentAsync(count: 2);

        Assert.That(await _time.NextWaitAsync(), Is.EqualTo(TimeSpan.FromSeconds(20)));
    }

    [Test]
    public async Task Bybit_that_is_not_enabled_is_not_asked()
    {
        await StartAsync(bybit: false);
        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(wait, Is.EqualTo(ToNextLoad));
            await _sender.DidNotReceive().Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Bybit_that_is_unavailable_stops_the_load_until_after_the_next_whole_hour()
    {
        _sender.Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<LoadFundingPaymentsResult>(new TransientExchangeException("Bybit answered 502.")), NothingLeft);

        await StartAsync();
        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(async () =>
        {
            // The other contract would fail the same way after retries of its own.
            Assert.That(wait, Is.EqualTo(ToNextLoad));
            await _sender.Received(1).Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Rounds_follow_one_another_while_history_is_left_all_as_of_the_moment_the_load_began()
    {
        // The first page of a contract leaves more to load, the next ones nothing; every page takes a second of the clock.
        var loaded = new HashSet<Guid>();
        _sender.Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is not LoadFundingPaymentsCommand command)
                {
                    return Task.FromResult(LoadFundingPaymentsResult.NothingLeft);
                }

                _sent.Writer.TryWrite(command);
                _time.Advance(TimeSpan.FromSeconds(1));

                return Task.FromResult(loaded.Add(command.InstrumentId) ? LoadFundingPaymentsResult.MoreLeft : LoadFundingPaymentsResult.NothingLeft);
            });

        await StartAsync();
        IReadOnlyList<LoadFundingPaymentsCommand> sent = await SentAsync(count: 4);
        TimeSpan wait = await _time.NextWaitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.EqualTo(new[] { PageOf(Btc, Start), PageOf(Eth, Start), PageOf(Btc, Start), PageOf(Eth, Start) }));
            Assert.That(wait, Is.EqualTo(ToNextLoad - TimeSpan.FromSeconds(4)));
        });
    }

    [Test]
    public async Task The_next_load_starts_half_a_minute_after_the_next_whole_hour()
    {
        await StartAsync();
        await SentAsync(count: 2);
        TimeSpan wait = await _time.NextWaitAsync();
        _time.Advance(wait);

        IReadOnlyList<LoadFundingPaymentsCommand> sent = await SentAsync(count: 2);
        DateTimeOffset afterTheHour = Start.AddMinutes(50).AddSeconds(30);

        Assert.Multiple(() =>
        {
            Assert.That(wait, Is.EqualTo(ToNextLoad));
            Assert.That(sent, Is.EqualTo(new[] { PageOf(Btc, afterTheHour), PageOf(Eth, afterTheHour) }));
        });
    }

    [Test]
    public async Task The_start_loads_one_page_of_every_listed_perpetual_contract_a_year_back()
    {
        await StartAsync();

        IReadOnlyList<LoadFundingPaymentsCommand> sent = await SentAsync(count: 2);
        await _time.NextWaitAsync();

        Assert.Multiple(async () =>
        {
            // A dated futures contract, a spot pair and a delisted contract are not asked for.
            Assert.That(sent, Is.EqualTo(new[] { PageOf(Btc, Start), PageOf(Eth, Start) }));
            await _sender.Received(2).Send(Arg.Any<LoadFundingPaymentsCommand>(), Arg.Any<CancellationToken>());
        });
    }
}