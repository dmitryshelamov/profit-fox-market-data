using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities.Candles;

[TestFixture]
public class CandleSeriesTests
{
    private static readonly Guid InstrumentId = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset From = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Until = From.AddDays(1);

    private static Candle CandleAt(DateTimeOffset openTime)
    {
        return new Candle(openTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 1m);
    }

    [Test]
    public void A_candle_outside_the_loaded_range_is_rejected()
    {
        var series = CandleSeries.Create(InstrumentId, From, Until, [CandleAt(From)]);

        Assert.Multiple(() =>
        {
            Assert.That(() => CandleSeries.Create(InstrumentId, From, Until, [CandleAt(From.AddMinutes(-1))]), Throws.ArgumentException);
            Assert.That(() => CandleSeries.Create(InstrumentId, From, Until, [CandleAt(Until)]), Throws.ArgumentException);
            Assert.That(() => series.Cover(Until, Until.AddMinutes(5), [CandleAt(Until.AddMinutes(5))]), Throws.ArgumentException);
            Assert.That(series.CoveredUntil, Is.EqualTo(Until));
            Assert.That(series.NewCandles, Is.EqualTo(new[] { CandleAt(From) }));
        });
    }

    [Test]
    public void An_empty_range_is_rejected()
    {
        var series = CandleSeries.Create(InstrumentId, From, Until, []);

        Assert.Multiple(() =>
        {
            Assert.That(() => CandleSeries.Create(InstrumentId, From, From, []), Throws.ArgumentException);
            Assert.That(() => CandleSeries.Create(InstrumentId, Until, From, []), Throws.ArgumentException);
            Assert.That(() => series.Cover(Until, Until, []), Throws.ArgumentException);
        });
    }

    [Test]
    public void Cover_grows_back_by_older_history_that_ends_at_the_start()
    {
        var series = CandleSeries.Create(InstrumentId, From, Until, [CandleAt(From)]);

        series.Cover(From.AddMinutes(-1000), From, [CandleAt(From.AddMinutes(-1000)), CandleAt(From.AddMinutes(-1))]);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(From.AddMinutes(-1000)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Until));
            Assert.That(series.NewCandles, Is.EqualTo(new[] { CandleAt(From), CandleAt(From.AddMinutes(-1000)), CandleAt(From.AddMinutes(-1)) }));
        });
    }

    [Test]
    public void Cover_grows_forward_by_a_range_that_starts_at_the_border()
    {
        var series = CandleSeries.Create(InstrumentId, From, Until, []);

        series.Cover(Until, Until.AddMinutes(5), [CandleAt(Until.AddMinutes(4))]);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(From));
            Assert.That(series.CoveredUntil, Is.EqualTo(Until.AddMinutes(5)));
            Assert.That(series.NewCandles, Is.EqualTo(new[] { CandleAt(Until.AddMinutes(4)) }));
        });
    }

    [Test]
    public void Cover_leaves_out_a_range_apart_from_the_coverage_with_its_candles()
    {
        var series = CandleSeries.Create(InstrumentId, From, Until, []);

        series.Cover(Until.AddMinutes(1), Until.AddMinutes(2), [CandleAt(Until.AddMinutes(1))]);
        series.Cover(From.AddMinutes(-2), From.AddMinutes(-1), [CandleAt(From.AddMinutes(-2))]);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(From));
            Assert.That(series.CoveredUntil, Is.EqualTo(Until));
            Assert.That(series.NewCandles, Is.Empty);
        });
    }

    [Test]
    public void Cover_takes_an_overlapping_range_and_keeps_what_it_held()
    {
        var series = CandleSeries.Create(InstrumentId, From, Until, []);

        series.Cover(From.AddHours(1), From.AddHours(2), []);
        series.Cover(Until.AddHours(-1), Until.AddHours(1), []);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(From));
            Assert.That(series.CoveredUntil, Is.EqualTo(Until.AddHours(1)));
        });
    }

    [Test]
    public void Create_covers_the_first_loaded_range_with_its_candles()
    {
        var series = CandleSeries.Create(InstrumentId, From, Until, [CandleAt(From), CandleAt(Until.AddMinutes(-1))]);

        Assert.Multiple(() =>
        {
            Assert.That(series.InstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(series.CoveredFrom, Is.EqualTo(From));
            Assert.That(series.CoveredUntil, Is.EqualTo(Until));
            Assert.That(series.NewCandles, Is.EqualTo(new[] { CandleAt(From), CandleAt(Until.AddMinutes(-1)) }));
        });
    }
}