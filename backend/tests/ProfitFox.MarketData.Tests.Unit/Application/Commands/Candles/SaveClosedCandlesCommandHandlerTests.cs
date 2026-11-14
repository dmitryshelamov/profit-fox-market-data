using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Candles;

[TestFixture]
public class SaveClosedCandlesCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _series = Substitute.For<ICandleSeriesRepository>();
        _feed = Substitute.For<IClosedCandleFeed>();
        _stored = [];
        _saved = null;
        _published = [];
        _series.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call[0] is IReadOnlyCollection<Guid> asked ? _stored.Where(series => asked.Contains(series.InstrumentId)).ToArray() : []);
        _series.SaveAsync(Arg.Do<IReadOnlyCollection<CandleSeries>>(series => _saved = series), Arg.Any<CancellationToken>());

        // What is told to the feed, with whether storage held it by then.
        _feed.Publish(Arg.Do<IReadOnlyCollection<ClosedCandle>>(candles => _published.Add((candles, SavedBefore: _saved is not null))));

        _handler = new SaveClosedCandlesCommandHandler(_series, _feed);
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly Guid Eth = Guid.Parse("0193a1f0-0000-7000-8000-000000000002");

    private ICandleSeriesRepository _series = null!;
    private IClosedCandleFeed _feed = null!;
    private List<(IReadOnlyCollection<ClosedCandle> Candles, bool SavedBefore)> _published = null!;
    private List<CandleSeries> _stored = null!;
    private IReadOnlyCollection<CandleSeries>? _saved;
    private SaveClosedCandlesCommandHandler _handler = null!;

    private void Stored(Guid instrumentId, MinuteRange covered)
    {
        var series = CandleSeries.Create(instrumentId);
        series.Cover(covered, []);
        _stored.Add(series);
    }

    private static ClosedCandle Closed(Guid instrumentId, Candle candle)
    {
        return new ClosedCandle(Exchange.Bybit, instrumentId, candle);
    }

    private Task<SaveClosedCandlesResult> Save(params ClosedCandle[] candles)
    {
        return _handler.Handle(new SaveClosedCandlesCommand(candles), CancellationToken.None);
    }

    private CandleSeries SavedOf(Guid instrumentId)
    {
        return _saved!.Single(series => series.InstrumentId == instrumentId);
    }

    [Test]
    public async Task A_minute_apart_from_the_covered_ranges_is_covered_on_its_own()
    {
        Stored(Btc, new MinuteRange(Minute.AddMinutes(-10), Minute.AddMinutes(-5)));

        await Save(Closed(Btc, CandleGenerator.Generate(Minute)));

        Assert.That(
            SavedOf(Btc).Covered,
            Is.EqualTo(new[] { new MinuteRange(Minute.AddMinutes(-10), Minute.AddMinutes(-5)), new MinuteRange(Minute, Minute.AddMinutes(1)) }));
    }

    [Test]
    public async Task A_minute_told_more_than_once_is_saved_as_it_was_told_last()
    {
        Stored(Btc, new MinuteRange(Minute.AddMinutes(-10), Minute));
        Candle again = CandleGenerator.Generate(Minute, volume: 3m, buyVolume: 2m);

        SaveClosedCandlesResult result = await Save(Closed(Btc, CandleGenerator.Generate(Minute, volume: 3m)), Closed(Btc, again));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 1)));
            Assert.That(SavedOf(Btc).NewCandles, Is.EqualTo(new[] { again }));
        });
    }

    [Test]
    public async Task A_minute_told_again_without_a_buy_volume_keeps_the_one_it_was_told_with()
    {
        Stored(Btc, new MinuteRange(Minute.AddMinutes(-10), Minute));
        Candle complete = CandleGenerator.Generate(Minute, volume: 3m, buyVolume: 2m);

        SaveClosedCandlesResult result = await Save(Closed(Btc, complete), Closed(Btc, CandleGenerator.Generate(Minute, volume: 3m)));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 1)));
            Assert.That(SavedOf(Btc).NewCandles, Is.EqualTo(new[] { complete }));
        });
    }

    [Test]
    public async Task Candles_of_series_not_started_are_left_out()
    {
        Stored(Btc, new MinuteRange(Minute.AddMinutes(-10), Minute));

        SaveClosedCandlesResult result = await Save(
            Closed(Btc, CandleGenerator.Generate(Minute)),
            Closed(Eth, CandleGenerator.Generate(Minute)));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 1)));
            Assert.That(_saved!.Select(series => series.InstrumentId), Is.EqualTo(new[] { Btc }));
            Assert.That(_published.Single().Candles.Select(closed => closed.InstrumentId), Is.EqualTo(new[] { Btc }));
        });
    }

    [Test]
    public async Task The_candles_of_several_instruments_are_saved_together_each_with_its_minute_covered()
    {
        Stored(Btc, new MinuteRange(Minute.AddMinutes(-10), Minute));
        Stored(Eth, new MinuteRange(Minute.AddMinutes(-10), Minute));
        Candle btc = CandleGenerator.Generate(Minute, volume: 3m, buyVolume: 2m);
        Candle eth = CandleGenerator.Generate(Minute, close: 6m);
        Candle ethNext = CandleGenerator.Generate(Minute.AddMinutes(1), close: 7m);

        SaveClosedCandlesResult result = await Save(Closed(Btc, btc), Closed(Eth, eth), Closed(Eth, ethNext));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 3)));
            Assert.That(SavedOf(Btc).NewCandles, Is.EqualTo(new[] { btc }));
            Assert.That(SavedOf(Btc).Covered, Is.EqualTo(new[] { new MinuteRange(Minute.AddMinutes(-10), Minute.AddMinutes(1)) }));
            Assert.That(SavedOf(Eth).NewCandles, Is.EqualTo(new[] { eth, ethNext }));
            Assert.That(SavedOf(Eth).Covered, Is.EqualTo(new[] { new MinuteRange(Minute.AddMinutes(-10), Minute.AddMinutes(2)) }));

            // The feed hears of the saved candles once, after they are saved.
            Assert.That(_published.Single().SavedBefore, Is.True);
            Assert.That(_published.Single().Candles, Is.EquivalentTo(new[] { Closed(Btc, btc), Closed(Eth, eth), Closed(Eth, ethNext) }));
        });
    }

    [Test]
    public async Task When_no_series_is_started_nothing_is_saved()
    {
        SaveClosedCandlesResult result = await Save(Closed(Btc, CandleGenerator.Generate(Minute)));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 0)));
            Assert.That(_saved, Is.Null);
            Assert.That(_published, Is.Empty);
        });
    }
}