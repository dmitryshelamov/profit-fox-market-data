using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Candles;

[TestFixture]
public class LoadCandlesCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _instruments = Substitute.For<IInstrumentRepository>();
        _series = Substitute.For<ICandleSeriesRepository>();
        _bybit = Substitute.For<IBybitCandles>();
        _tinvest = Substitute.For<ITInvestCandles>();
        _saved = null;

        _instruments.GetAsync(Linear.Id, Arg.Any<CancellationToken>()).Returns(Linear);
        _instruments.GetAsync(Share.Id, Arg.Any<CancellationToken>()).Returns(Share);
        _bybit.GetAsync(Arg.Any<BybitListedInstrument>(), Arg.Any<MinuteRange>(), Arg.Any<CancellationToken>()).Returns([]);
        _tinvest.GetAsync(Arg.Any<TInvestListedShare>(), Arg.Any<MinuteRange>(), Arg.Any<CancellationToken>()).Returns([]);
        _series.SaveAsync(Arg.Do<CandleSeries>(series => _saved = series), Arg.Any<CancellationToken>());

        _handler = new LoadCandlesCommandHandler(_instruments, _series, _bybit, _tinvest);
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());

    private static readonly BybitListedInstrument LinearListing = (BybitListedInstrument)Linear.Listing;

    private static readonly TInvestListedShare ShareListing = (TInvestListedShare)Share.Listing;

    private IInstrumentRepository _instruments = null!;
    private ICandleSeriesRepository _series = null!;
    private IBybitCandles _bybit = null!;
    private ITInvestCandles _tinvest = null!;
    private CandleSeries? _saved;
    private LoadCandlesCommandHandler _handler = null!;

    private Task<bool> Load(Instrument instrument, MinuteRange range, DateTimeOffset? requestedAt = null)
    {
        return _handler.Handle(new LoadCandlesCommand(instrument.Id, range, requestedAt ?? Now.AddSeconds(30)), CancellationToken.None);
    }

    private void Stored(Instrument instrument, MinuteRange covered)
    {
        var series = CandleSeries.Create(instrument.Id);
        series.Cover(covered, []);
        _series.GetAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(series);
    }

    private static Candle CandleAt(int minute)
    {
        return new Candle(Now.AddMinutes(minute), open: 5m, high: 10m, low: 1m, close: 5m, volume: 1m);
    }

    private static MinuteRange Minutes(int from, int until)
    {
        return new MinuteRange(Now.AddMinutes(from), Now.AddMinutes(until));
    }

    [Test]
    public async Task A_long_uncovered_part_is_loaded_a_page_from_its_end()
    {
        Stored(Linear, Minutes(from: -10, until: 0));
        Stored(Share, Minutes(from: -10, until: 0));

        await Load(Linear, Minutes(from: -5000, until: 0));
        await Load(Share, Minutes(from: -5000, until: 0));

        Assert.Multiple(async () =>
        {
            await _bybit.Received(1).GetAsync(LinearListing, Minutes(from: -1010, until: -10), Arg.Any<CancellationToken>());
            await _tinvest.Received(1).GetAsync(ShareListing, Minutes(from: -1450, until: -10), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task A_minute_is_loaded_only_five_seconds_after_it_closed()
    {
        Stored(Linear, Minutes(from: -100, until: -1));

        bool early = await Load(Linear, Minutes(from: -100, until: 0), Now.AddSeconds(4));
        bool settled = await Load(Linear, Minutes(from: -100, until: 0), Now.AddSeconds(5));

        Assert.Multiple(async () =>
        {
            Assert.That(early, Is.False);
            Assert.That(settled, Is.False);
            await _bybit.Received(1).GetAsync(Arg.Any<BybitListedInstrument>(), Arg.Any<MinuteRange>(), Arg.Any<CancellationToken>());
            await _bybit.Received(1).GetAsync(LinearListing, Minutes(from: -1, until: 0), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task A_new_series_covers_the_time_before_trading_started_with_no_candles()
    {
        _bybit.GetTradingStartAsync(LinearListing, Now.AddMinutes(-3000), Arg.Any<CancellationToken>()).Returns(Now.AddMinutes(-100).AddSeconds(30));

        bool left = await Load(Linear, Minutes(from: -3000, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.False);
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -3000, until: 0) }));
            await _bybit.Received(1).GetAsync(LinearListing, Minutes(from: -100, until: 0), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task A_new_series_of_an_instrument_that_starts_after_the_range_is_saved_without_asking_for_candles()
    {
        _bybit.GetTradingStartAsync(LinearListing, Now.AddMinutes(-3000), Arg.Any<CancellationToken>()).Returns(Now.AddDays(1));

        bool left = await Load(Linear, Minutes(from: -3000, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.False);
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -3000, until: 0) }));
            await _bybit.DidNotReceiveWithAnyArgs().GetAsync(default!, default!, cancellationToken: default);
        });
    }

    [Test]
    public async Task A_new_series_without_a_known_start_loads_the_newest_page()
    {
        bool left = await Load(Share, Minutes(from: -3000, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.True);
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -1440, until: 0) }));
            await _tinvest.Received(1).GetAsync(ShareListing, Minutes(from: -1440, until: 0), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_rejected()
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(
            new LoadCandlesCommand(Guid.NewGuid(), Minutes(from: -10, until: 0), Now),
            CancellationToken.None));
    }

    [Test]
    public async Task Nothing_left_to_load_gives_false_without_asking_the_exchange_or_saving()
    {
        Stored(Linear, Minutes(from: -100, until: 0));

        bool left = await Load(Linear, Minutes(from: -50, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.False);
            await _bybit.DidNotReceiveWithAnyArgs().GetAsync(default!, default!, cancellationToken: default);
            await _series.DidNotReceiveWithAnyArgs().SaveAsync(default!, cancellationToken: default);
        });
    }

    [Test]
    public async Task The_newest_uncovered_part_is_loaded_first_and_saved_with_its_candles()
    {
        Stored(Linear, Minutes(from: -2000, until: -10));
        _bybit.GetAsync(LinearListing, Minutes(from: -10, until: 0), Arg.Any<CancellationToken>()).Returns([CandleAt(-5)]);

        bool left = await Load(Linear, Minutes(from: -3000, until: 0));

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.True);
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -2000, until: 0) }));
            Assert.That(_saved.NewCandles, Is.EqualTo(new[] { CandleAt(-5) }));
        });
    }
}