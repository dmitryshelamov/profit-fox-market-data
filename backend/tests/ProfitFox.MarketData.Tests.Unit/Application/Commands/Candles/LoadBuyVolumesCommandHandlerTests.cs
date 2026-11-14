using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Candles.LoadBuyVolumes;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Candles;

[TestFixture]
public class LoadBuyVolumesCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _instruments = Substitute.For<IInstrumentRepository>();
        _series = Substitute.For<ICandleSeriesRepository>();
        _archive = Substitute.For<IBybitTradeArchiveClient>();
        _saved = null;

        _instruments.GetAsync(Linear.Id, Arg.Any<CancellationToken>()).Returns(Linear);
        _instruments.GetAsync(Share.Id, Arg.Any<CancellationToken>()).Returns(Share);
        _series.GetCandlesWithoutBuyVolumeAsync(Arg.Any<Guid>(), Arg.Any<MinuteRange>(), Arg.Any<CancellationToken>()).Returns([]);
        _series.SaveAsync(Arg.Do<CandleSeries>(series => _saved = series), Arg.Any<CancellationToken>());

        _handler = new LoadBuyVolumesCommandHandler(_instruments, _series, _archive);
    }

    // Three whole days are behind: the 9th, the 10th and the 11th; the archive of the 11th was published hours ago.
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 30, TimeSpan.Zero);

    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly MinuteRange History = new MinuteRange(Today.AddDays(-3), Today.AddHours(9));

    private static readonly Instrument Linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());

    private IInstrumentRepository _instruments = null!;
    private ICandleSeriesRepository _series = null!;
    private IBybitTradeArchiveClient _archive = null!;
    private CandleSeries? _saved;
    private LoadBuyVolumesCommandHandler _handler = null!;

    private Task<LoadBuyVolumesResult> Load(Instrument instrument, DateTimeOffset? requestedAt = null)
    {
        return _handler.Handle(new LoadBuyVolumesCommand(instrument.Id, History, requestedAt ?? Now), CancellationToken.None);
    }

    // The day that starts so many days before today.
    private static MinuteRange Day(int daysAgo)
    {
        return new MinuteRange(Today.AddDays(-daysAgo), Today.AddDays(1 - daysAgo));
    }

    private static DateOnly DateOf(int daysAgo)
    {
        return DateOnly.FromDateTime(Today.AddDays(-daysAgo).UtcDateTime);
    }

    private CandleSeries Stored(MinuteRange covered)
    {
        var series = CandleSeries.Create(Linear.Id);
        series.Cover(covered, []);
        _series.GetAsync(Linear.Id, Arg.Any<CancellationToken>()).Returns(series);

        return series;
    }

    private void Without(int daysAgo, params Candle[] candles)
    {
        _series.GetCandlesWithoutBuyVolumeAsync(Linear.Id, Day(daysAgo), Arg.Any<CancellationToken>()).Returns(candles);
    }

    // The archive of the day tells the buy volume of the candle; null leaves it as it is.
    private void Archive(int daysAgo, params (Candle Candle, decimal? BuyVolume)[] told)
    {
        _archive.FillBuyVolumesAsync((BybitListedInstrument)Linear.Listing, DateOf(daysAgo), Arg.Any<IReadOnlyCollection<Candle>>(), Arg.Any<CancellationToken>())
            .Returns([.. told.Select(one => CandleGenerator.Generate(one.Candle.OpenTime, volume: one.Candle.Volume, buyVolume: one.BuyVolume))]);
    }

    private void NoArchive(int daysAgo)
    {
        _archive.FillBuyVolumesAsync(Arg.Any<BybitListedInstrument>(), DateOf(daysAgo), Arg.Any<IReadOnlyCollection<Candle>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Candle>?)null);
    }

    [Test]
    public async Task A_candle_the_archive_does_not_add_up_for_stays_without_a_buy_volume_and_its_day_is_settled()
    {
        Stored(History);
        Candle candle = CandleGenerator.Generate(Day(daysAgo: 1).From, volume: 3m);
        Without(daysAgo: 1, candle);
        Archive(daysAgo: 1, (candle, null));

        await Load(Linear);

        Assert.Multiple(() =>
        {
            Assert.That(_saved?.NewCandles, Is.Empty);
            Assert.That(_saved?.Settled, Is.EqualTo(new[] { Day(daysAgo: 1) }));
        });
    }

    [Test]
    public async Task A_day_not_all_covered_waits_for_its_candles()
    {
        Stored(new MinuteRange(Today.AddDays(-2).AddHours(12), Today.AddHours(9)));

        LoadBuyVolumesResult result = await Load(Linear);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadBuyVolumesResult.NothingLeft));
            Assert.That(_saved?.Settled, Is.EqualTo(new[] { Day(daysAgo: 1) }));
        });
    }

    [Test]
    public async Task A_day_that_ended_less_than_two_hours_ago_is_not_asked_for_yet()
    {
        Stored(History);
        Without(daysAgo: 1, CandleGenerator.Generate(Day(daysAgo: 1).From, volume: 3m));

        await Load(Linear, Today.AddHours(1));

        Assert.Multiple(async () =>
        {
            Assert.That(_saved?.Settled, Is.EqualTo(new[] { new MinuteRange(Today.AddDays(-3), Today.AddDays(-1)) }));
            await _archive.DidNotReceiveWithAnyArgs().FillBuyVolumesAsync(default!, default, default!, cancellationToken: default);
        });
    }

    [Test]
    public async Task A_day_whose_archive_is_not_published_yet_is_left_and_an_older_one_goes_on()
    {
        Stored(History);
        Candle fresh = CandleGenerator.Generate(Day(daysAgo: 1).From, volume: 3m);
        Candle older = CandleGenerator.Generate(Day(daysAgo: 2).From, volume: 4m);
        Without(daysAgo: 1, fresh);
        Without(daysAgo: 2, older);
        NoArchive(daysAgo: 1);
        Archive(daysAgo: 2, (older, 1m));

        LoadBuyVolumesResult result = await Load(Linear);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadBuyVolumesResult.MoreLeft));
            Assert.That(_saved?.Settled, Is.EqualTo(new[] { Day(daysAgo: 2) }));
            Assert.That(_saved?.NewCandles, Is.EqualTo(new[] { CandleGenerator.Generate(older.OpenTime, volume: 4m, buyVolume: 1m) }));
        });
    }

    [Test]
    public async Task A_day_whose_archive_is_still_not_there_two_days_after_it_ended_is_settled_without_it()
    {
        Stored(new MinuteRange(Today.AddDays(-3), Today.AddDays(2)));
        Without(daysAgo: 1, CandleGenerator.Generate(Day(daysAgo: 1).From, volume: 3m));
        NoArchive(daysAgo: 1);

        await Load(Linear, Today.AddDays(2).AddMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(_saved?.Settled, Is.EqualTo(new[] { Day(daysAgo: 1) }));
            Assert.That(_saved?.NewCandles, Is.Empty);
        });
    }

    [Test]
    public async Task A_series_not_started_has_nothing_to_load()
    {
        LoadBuyVolumesResult result = await Load(Linear);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadBuyVolumesResult.NothingLeft));
            Assert.That(_saved, Is.Null);
        });
    }

    [Test]
    public async Task An_instrument_of_another_exchange_has_nothing_to_load()
    {
        LoadBuyVolumesResult result = await Load(Share);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadBuyVolumesResult.NothingLeft));
            await _series.DidNotReceiveWithAnyArgs().GetAsync(default(Guid), cancellationToken: default);
        });
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_not_found()
    {
        Assert.ThrowsAsync<InstrumentNotFoundException>(
            () => _handler.Handle(new LoadBuyVolumesCommand(Guid.NewGuid(), History, Now), CancellationToken.None));
    }

    [Test]
    public async Task Days_that_need_no_archive_are_settled_together_and_nothing_is_left()
    {
        Stored(History);
        Candle quiet = CandleGenerator.Generate(Day(daysAgo: 2).From, volume: 0m);
        Without(daysAgo: 2, quiet);

        LoadBuyVolumesResult result = await Load(Linear);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadBuyVolumesResult.NothingLeft));
            Assert.That(_saved?.Settled, Is.EqualTo(new[] { new MinuteRange(Today.AddDays(-3), Today) }));
            Assert.That(_saved?.NewCandles, Is.EqualTo(new[] { CandleGenerator.Generate(quiet.OpenTime, volume: 0m, buyVolume: 0m) }));
            await _series.Received(1).SaveAsync(Arg.Any<CandleSeries>(), Arg.Any<CancellationToken>());
            await _archive.DidNotReceiveWithAnyArgs().FillBuyVolumesAsync(default!, default, default!, cancellationToken: default);
        });
    }

    [Test]
    public async Task Settled_days_are_not_looked_at_again()
    {
        CandleSeries series = Stored(History);
        series.Settle(new MinuteRange(Today.AddDays(-3), Today));

        LoadBuyVolumesResult result = await Load(Linear);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadBuyVolumesResult.NothingLeft));
            Assert.That(_saved, Is.Null);
            await _series.DidNotReceiveWithAnyArgs().GetCandlesWithoutBuyVolumeAsync(default, default!, cancellationToken: default);
        });
    }

    [Test]
    public async Task The_newest_day_takes_its_buy_volumes_from_the_archive_and_is_settled_with_them()
    {
        Stored(History);
        Candle traded = CandleGenerator.Generate(Day(daysAgo: 1).From, volume: 3m);
        Candle quiet = CandleGenerator.Generate(Day(daysAgo: 1).From.AddMinutes(1), volume: 0m);
        Without(daysAgo: 1, traded, quiet);
        Archive(daysAgo: 1, (traded, 2m));

        LoadBuyVolumesResult result = await Load(Linear);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadBuyVolumesResult.MoreLeft));
            Assert.That(_saved?.Settled, Is.EqualTo(new[] { Day(daysAgo: 1) }));
            Assert.That(
                _saved?.NewCandles,
                Is.EquivalentTo(
                    new[]
                    {
                        CandleGenerator.Generate(traded.OpenTime, volume: 3m, buyVolume: 2m),
                        CandleGenerator.Generate(quiet.OpenTime, volume: 0m, buyVolume: 0m)
                    }));
            await _archive.Received(1).FillBuyVolumesAsync(
                (BybitListedInstrument)Linear.Listing,
                DateOf(daysAgo: 1),
                Arg.Is<IReadOnlyCollection<Candle>>(asked => asked.SequenceEqual(new[] { traded })),
                Arg.Any<CancellationToken>());
        });
    }
}