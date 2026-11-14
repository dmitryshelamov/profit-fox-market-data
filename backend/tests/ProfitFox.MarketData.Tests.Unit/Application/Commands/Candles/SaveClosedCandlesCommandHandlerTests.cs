using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;
using ProfitFox.MarketData.Domain.Entities.Candles;
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
        _stored = [];
        _saved = null;
        _series.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => _stored.Where(series => call.Arg<IReadOnlyCollection<Guid>>().Contains(series.InstrumentId)).ToArray());
        _series.SaveAsync(Arg.Do<IReadOnlyCollection<CandleSeries>>(series => _saved = series), Arg.Any<CancellationToken>());

        _handler = new SaveClosedCandlesCommandHandler(_series);
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly Guid Eth = Guid.Parse("0193a1f0-0000-7000-8000-000000000002");

    private ICandleSeriesRepository _series = null!;
    private List<CandleSeries> _stored = null!;
    private IReadOnlyCollection<CandleSeries>? _saved;
    private SaveClosedCandlesCommandHandler _handler = null!;

    private void Stored(Guid instrumentId, MinuteRange covered)
    {
        var series = CandleSeries.Create(instrumentId);
        series.Cover(covered, []);
        _stored.Add(series);
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

        await Save(new ClosedCandle(Btc, CandleGenerator.Generate(Minute)));

        Assert.That(
            SavedOf(Btc).Covered,
            Is.EqualTo(new[] { new MinuteRange(Minute.AddMinutes(-10), Minute.AddMinutes(-5)), new MinuteRange(Minute, Minute.AddMinutes(1)) }));
    }

    [Test]
    public async Task A_minute_told_more_than_once_is_saved_as_it_was_told_last()
    {
        Stored(Btc, new MinuteRange(Minute.AddMinutes(-10), Minute));
        Candle again = CandleGenerator.Generate(Minute, volume: 3m, buyVolume: 2m);

        SaveClosedCandlesResult result = await Save(new ClosedCandle(Btc, CandleGenerator.Generate(Minute, volume: 3m)), new ClosedCandle(Btc, again));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 1)));
            Assert.That(SavedOf(Btc).NewCandles, Is.EqualTo(new[] { again }));
        });
    }

    [Test]
    public async Task Candles_of_series_not_started_are_left_out()
    {
        Stored(Btc, new MinuteRange(Minute.AddMinutes(-10), Minute));

        SaveClosedCandlesResult result = await Save(
            new ClosedCandle(Btc, CandleGenerator.Generate(Minute)),
            new ClosedCandle(Eth, CandleGenerator.Generate(Minute)));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 1)));
            Assert.That(_saved!.Select(series => series.InstrumentId), Is.EqualTo(new[] { Btc }));
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

        SaveClosedCandlesResult result = await Save(new ClosedCandle(Btc, btc), new ClosedCandle(Eth, eth), new ClosedCandle(Eth, ethNext));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 3)));
            Assert.That(SavedOf(Btc).NewCandles, Is.EqualTo(new[] { btc }));
            Assert.That(SavedOf(Btc).Covered, Is.EqualTo(new[] { new MinuteRange(Minute.AddMinutes(-10), Minute.AddMinutes(1)) }));
            Assert.That(SavedOf(Eth).NewCandles, Is.EqualTo(new[] { eth, ethNext }));
            Assert.That(SavedOf(Eth).Covered, Is.EqualTo(new[] { new MinuteRange(Minute.AddMinutes(-10), Minute.AddMinutes(2)) }));
        });
    }

    [Test]
    public async Task When_no_series_is_started_nothing_is_saved()
    {
        SaveClosedCandlesResult result = await Save(new ClosedCandle(Btc, CandleGenerator.Generate(Minute)));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SaveClosedCandlesResult(Saved: 0)));
            Assert.That(_saved, Is.Null);
        });
    }
}