using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;
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
        _bybit = Client(Exchange.Bybit, TimeSpan.FromMinutes(1000));
        _tinvest = Client(Exchange.TInvest, TimeSpan.FromDays(1));
        _saved = null;

        _instruments.GetAsync(Linear.Id, Arg.Any<CancellationToken>()).Returns(Linear);
        _instruments.GetAsync(Share.Id, Arg.Any<CancellationToken>()).Returns(Share);
        _series.SaveAsync(Arg.Do<CandleSeries>(series => _saved = series), Arg.Any<CancellationToken>());

        _handler = new LoadCandlesCommandHandler(_instruments, _series, [_bybit, _tinvest]);
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());

    private static readonly ListedInstrument LinearListing = Linear.Listing;

    private static readonly ListedInstrument ShareListing = Share.Listing;

    private IInstrumentRepository _instruments = null!;
    private ICandleSeriesRepository _series = null!;
    private IExchangeCandlesClient _bybit = null!;
    private IExchangeCandlesClient _tinvest = null!;
    private CandleSeries? _saved;
    private LoadCandlesCommandHandler _handler = null!;

    // The client of an exchange: it gives pages of its length and has no candles unless a test says so.
    private static IExchangeCandlesClient Client(Exchange exchange, TimeSpan pageLength)
    {
        IExchangeCandlesClient client = Substitute.For<IExchangeCandlesClient>();
        client.Exchange.Returns(exchange);
        client.PageLength.Returns(pageLength);
        client.GetAsync(Arg.Any<ListedInstrument>(), Arg.Any<MinuteRange>(), Arg.Any<CancellationToken>()).Returns([]);

        return client;
    }

    private Task<LoadCandlesResult> Load(Instrument instrument, MinuteRange range, DateTimeOffset? requestedAt = null)
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
        return CandleGenerator.Generate(Now.AddMinutes(minute));
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

        LoadCandlesResult early = await Load(Linear, Minutes(from: -100, until: 0), Now.AddSeconds(4));
        LoadCandlesResult settled = await Load(Linear, Minutes(from: -100, until: 0), Now.AddSeconds(5));

        Assert.Multiple(async () =>
        {
            Assert.That(early, Is.EqualTo(LoadCandlesResult.NothingLeft));
            Assert.That(settled, Is.EqualTo(LoadCandlesResult.NothingLeft));
            await _bybit.Received(1).GetAsync(Arg.Any<ListedInstrument>(), Arg.Any<MinuteRange>(), Arg.Any<CancellationToken>());
            await _bybit.Received(1).GetAsync(LinearListing, Minutes(from: -1, until: 0), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task A_new_series_covers_the_time_before_trading_started_with_no_candles()
    {
        _bybit.GetTradingStartAsync(LinearListing, Now.AddMinutes(-3000), Arg.Any<CancellationToken>()).Returns(Now.AddMinutes(-100).AddSeconds(30));

        LoadCandlesResult left = await Load(Linear, Minutes(from: -3000, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.EqualTo(LoadCandlesResult.NothingLeft));
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -3000, until: 0) }));
            await _bybit.Received(1).GetAsync(LinearListing, Minutes(from: -100, until: 0), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task A_new_series_of_an_instrument_that_starts_after_the_range_is_saved_without_asking_for_candles()
    {
        _bybit.GetTradingStartAsync(LinearListing, Now.AddMinutes(-3000), Arg.Any<CancellationToken>()).Returns(Now.AddDays(1));

        LoadCandlesResult left = await Load(Linear, Minutes(from: -3000, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.EqualTo(LoadCandlesResult.NothingLeft));
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -3000, until: 0) }));
            await _bybit.DidNotReceiveWithAnyArgs().GetAsync(default!, default!, cancellationToken: default);
        });
    }

    [Test]
    public async Task A_new_series_without_a_known_start_loads_the_newest_page()
    {
        LoadCandlesResult left = await Load(Share, Minutes(from: -3000, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.EqualTo(LoadCandlesResult.MoreLeft));
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -1440, until: 0) }));
            await _tinvest.Received(1).GetAsync(ShareListing, Minutes(from: -1440, until: 0), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_rejected()
    {
        Assert.ThrowsAsync<InstrumentNotFoundException>(() => _handler.Handle(
            new LoadCandlesCommand(Guid.NewGuid(), Minutes(from: -10, until: 0), Now),
            CancellationToken.None));
    }

    [Test]
    public async Task Nothing_left_to_load_asks_no_exchange_and_saves_nothing()
    {
        Stored(Linear, Minutes(from: -100, until: 0));

        LoadCandlesResult left = await Load(Linear, Minutes(from: -50, until: 0));

        Assert.Multiple(async () =>
        {
            Assert.That(left, Is.EqualTo(LoadCandlesResult.NothingLeft));
            await _bybit.DidNotReceiveWithAnyArgs().GetAsync(default!, default!, cancellationToken: default);
            await _series.DidNotReceiveWithAnyArgs().SaveAsync(default(CandleSeries)!, cancellationToken: default);
        });
    }

    [Test]
    public async Task The_newest_uncovered_part_is_loaded_first_and_saved_with_its_candles()
    {
        Stored(Linear, Minutes(from: -2000, until: -10));
        _bybit.GetAsync(LinearListing, Minutes(from: -10, until: 0), Arg.Any<CancellationToken>()).Returns([CandleAt(-5)]);

        LoadCandlesResult left = await Load(Linear, Minutes(from: -3000, until: 0));

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.EqualTo(LoadCandlesResult.MoreLeft));
            Assert.That(_saved!.Covered, Is.EqualTo(new[] { Minutes(from: -2000, until: 0) }));
            Assert.That(_saved.NewCandles, Is.EqualTo(new[] { CandleAt(-5) }));
        });
    }
}