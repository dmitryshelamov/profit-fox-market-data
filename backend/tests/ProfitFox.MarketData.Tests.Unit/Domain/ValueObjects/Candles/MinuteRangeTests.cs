using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Candles;

[TestFixture]
public class MinuteRangeTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void Join_gives_the_one_range_from_the_earlier_start_to_the_later_end()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Minutes(from: 10, until: 20).Join(Minutes(from: 20, until: 30)), Is.EqualTo(Minutes(from: 10, until: 30)));
            Assert.That(Minutes(from: 10, until: 20).Join(Minutes(from: 5, until: 12)), Is.EqualTo(Minutes(from: 5, until: 20)));
            Assert.That(Minutes(from: 10, until: 20).Join(Minutes(from: 12, until: 15)), Is.EqualTo(Minutes(from: 10, until: 20)));
        });
    }

    [Test]
    public void Ranges_that_overlap_or_meet_end_to_start_touch_and_ranges_with_a_gap_do_not()
    {
        MinuteRange range = Minutes(from: 10, until: 20);

        Assert.Multiple(() =>
        {
            Assert.That(range.Touches(Minutes(from: 15, until: 30)), Is.True);
            Assert.That(range.Touches(Minutes(from: 20, until: 30)), Is.True);
            Assert.That(range.Touches(Minutes(from: 0, until: 10)), Is.True);
            Assert.That(range.Touches(Minutes(from: 21, until: 30)), Is.False);
            Assert.That(range.Touches(Minutes(from: 0, until: 9)), Is.False);
        });
    }

    [Test]
    public void The_start_of_a_minute_drops_its_seconds_whatever_the_offset()
    {
        var inside = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 14, minute: 30, second: 59, millisecond: 999, TimeSpan.FromHours(5.5));

        Assert.That(
            MinuteRange.StartOfMinute(inside),
            Is.EqualTo(new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero)));
    }

    private static MinuteRange Minutes(int from, int until)
    {
        return new MinuteRange(From.AddMinutes(from), From.AddMinutes(until));
    }

    [Test]
    public void An_empty_range_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new MinuteRange(From, From), Throws.ArgumentException);
            Assert.That(() => new MinuteRange(From, From.AddMinutes(-1)), Throws.ArgumentException);
        });
    }

    [Test]
    public void An_end_inside_a_minute_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new MinuteRange(From.AddSeconds(30), From.AddMinutes(5)), Throws.ArgumentException);
            Assert.That(() => new MinuteRange(From, From.AddMinutes(5).AddMilliseconds(1)), Throws.ArgumentException);
        });
    }

    [Test]
    public void Except_a_range_apart_or_touching_leaves_the_range_whole()
    {
        MinuteRange range = Minutes(from: 10, until: 20);

        Assert.Multiple(() =>
        {
            Assert.That(range.Except(Minutes(from: 0, until: 5)), Is.EqualTo(new[] { range }));
            Assert.That(range.Except(Minutes(from: 0, until: 10)), Is.EqualTo(new[] { range }));
            Assert.That(range.Except(Minutes(from: 20, until: 30)), Is.EqualTo(new[] { range }));
        });
    }

    [Test]
    public void Except_a_range_inside_leaves_the_parts_before_and_after_it()
    {
        Assert.That(Minutes(from: 10, until: 20).Except(Minutes(from: 12, until: 15)), Is.EqualTo(new[] { Minutes(from: 10, until: 12), Minutes(from: 15, until: 20) }));
    }

    [Test]
    public void Except_a_range_over_all_of_it_leaves_nothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Minutes(from: 10, until: 20).Except(Minutes(from: 10, until: 20)), Is.Empty);
            Assert.That(Minutes(from: 10, until: 20).Except(Minutes(from: 0, until: 30)), Is.Empty);
        });
    }

    [Test]
    public void Except_a_range_over_one_end_leaves_the_other_end()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Minutes(from: 10, until: 20).Except(Minutes(from: 0, until: 15)), Is.EqualTo(new[] { Minutes(from: 15, until: 20) }));
            Assert.That(Minutes(from: 10, until: 20).Except(Minutes(from: 15, until: 30)), Is.EqualTo(new[] { Minutes(from: 10, until: 15) }));
        });
    }
}