using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities.Candles;

[TestFixture]
public class CandleSeriesTests
{
    private static readonly Guid InstrumentId = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static Candle CandleAt(int minute)
    {
        return new Candle(Start.AddMinutes(minute), open: 5m, high: 10m, low: 1m, close: 5m, volume: 1m);
    }

    private static MinuteRange Minutes(int from, int until)
    {
        return new MinuteRange(Start.AddMinutes(from), Start.AddMinutes(until));
    }

    [Test]
    public void A_candle_outside_the_range_is_rejected_and_nothing_is_taken()
    {
        var series = CandleSeries.Create(InstrumentId);

        Assert.Multiple(() =>
        {
            Assert.That(() => series.Cover(Minutes(from: 10, until: 20), [CandleAt(9)]), Throws.ArgumentException);
            Assert.That(() => series.Cover(Minutes(from: 10, until: 20), [CandleAt(20)]), Throws.ArgumentException);
            Assert.That(series.Covered, Is.Empty);
            Assert.That(series.NewCandles, Is.Empty);
        });
    }

    [Test]
    public void A_new_series_misses_all_it_is_asked_for()
    {
        var series = CandleSeries.Create(InstrumentId);

        Assert.Multiple(() =>
        {
            Assert.That(series.InstrumentId, Is.EqualTo(InstrumentId));
            Assert.That(series.Covered, Is.Empty);
            Assert.That(series.Missing(Minutes(from: 0, until: 60)), Is.EqualTo(new[] { Minutes(from: 0, until: 60) }));
        });
    }

    [Test]
    public void Cover_collects_the_candles_of_every_range_it_takes()
    {
        var series = CandleSeries.Create(InstrumentId);

        series.Cover(Minutes(from: 10, until: 20), [CandleAt(10), CandleAt(19)]);
        series.Cover(Minutes(from: 40, until: 50), []);
        series.Cover(Minutes(from: 0, until: 5), [CandleAt(3)]);

        Assert.That(series.NewCandles, Is.EqualTo(new[] { CandleAt(10), CandleAt(19), CandleAt(3) }));
    }

    [Test]
    public void Cover_joins_a_range_that_bridges_two_into_one()
    {
        var series = CandleSeries.Create(InstrumentId);

        series.Cover(Minutes(from: 0, until: 10), []);
        series.Cover(Minutes(from: 20, until: 30), []);
        series.Cover(Minutes(from: 5, until: 25), []);

        Assert.That(series.Covered, Is.EqualTo(new[] { Minutes(from: 0, until: 30) }));
    }

    [Test]
    public void Cover_joins_ranges_that_touch()
    {
        var series = CandleSeries.Create(InstrumentId);

        series.Cover(Minutes(from: 10, until: 20), []);
        series.Cover(Minutes(from: 20, until: 30), []);
        series.Cover(Minutes(from: 0, until: 10), []);

        Assert.That(series.Covered, Is.EqualTo(new[] { Minutes(from: 0, until: 30) }));
    }

    [Test]
    public void Cover_keeps_ranges_apart_from_one_another_oldest_first()
    {
        var series = CandleSeries.Create(InstrumentId);

        series.Cover(Minutes(from: 40, until: 50), []);
        series.Cover(Minutes(from: 0, until: 10), []);
        series.Cover(Minutes(from: 20, until: 30), []);

        Assert.That(series.Covered, Is.EqualTo(new[] { Minutes(from: 0, until: 10), Minutes(from: 20, until: 30), Minutes(from: 40, until: 50) }));
    }

    [Test]
    public void Cover_of_a_range_inside_the_coverage_keeps_it_as_it_is()
    {
        var series = CandleSeries.Create(InstrumentId);

        series.Cover(Minutes(from: 0, until: 30), []);
        series.Cover(Minutes(from: 10, until: 20), []);

        Assert.That(series.Covered, Is.EqualTo(new[] { Minutes(from: 0, until: 30) }));
    }

    [Test]
    public void Missing_gives_nothing_for_a_covered_range()
    {
        var series = CandleSeries.Create(InstrumentId);
        series.Cover(Minutes(from: 0, until: 30), []);

        Assert.That(series.Missing(Minutes(from: 5, until: 30)), Is.Empty);
    }

    [Test]
    public void Missing_gives_the_parts_around_and_between_the_covered_ranges_oldest_first()
    {
        var series = CandleSeries.Create(InstrumentId);
        series.Cover(Minutes(from: 10, until: 20), []);
        series.Cover(Minutes(from: 30, until: 40), []);
        series.Cover(Minutes(from: 70, until: 80), []);

        Assert.That(
            series.Missing(Minutes(from: 0, until: 60)),
            Is.EqualTo(new[] { Minutes(from: 0, until: 10), Minutes(from: 20, until: 30), Minutes(from: 40, until: 60) }));
    }

    [Test]
    public void Missing_leaves_out_what_a_range_covers_at_either_end()
    {
        var series = CandleSeries.Create(InstrumentId);
        series.Cover(Minutes(from: 0, until: 10), []);
        series.Cover(Minutes(from: 50, until: 70), []);

        Assert.That(series.Missing(Minutes(from: 5, until: 60)), Is.EqualTo(new[] { Minutes(from: 10, until: 50) }));
    }
}