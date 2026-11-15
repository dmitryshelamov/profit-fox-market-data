using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities.OpenInterest;

[TestFixture]
public class OpenInterestSeriesTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly Guid Eth = Guid.Parse("0193a1f0-0000-7000-8000-000000000002");

    private static OpenInterestPoint At(DateTimeOffset time, Guid? instrumentId = null)
    {
        return new OpenInterestPoint(instrumentId ?? Btc, time, value: 52345.123m);
    }

    // The series that covers [11:00, 12:00).
    private static OpenInterestSeries Covering()
    {
        return OpenInterestSeries.Restore(Btc, Noon.AddHours(-1), Noon);
    }

    [Test]
    public void A_new_series_covers_its_first_range_and_keeps_the_points_of_it_to_save()
    {
        OpenInterestPoint[] points = [At(Noon.AddMinutes(-10)), At(Noon.AddMinutes(-5))];

        var series = OpenInterestSeries.Create(Btc, Noon.AddMinutes(-10), Noon, points);

        Assert.Multiple(() =>
        {
            Assert.That(series.InstrumentId, Is.EqualTo(Btc));
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddMinutes(-10)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon));
            Assert.That(series.NewPoints, Is.EqualTo(points));
        });
    }

    [Test]
    public void A_point_that_is_not_one_of_the_range_is_rejected()
    {
        Assert.Multiple(() =>
        {
            // At the end of the range, before its start, between two steps, of another instrument.
            Assert.That(() => Covering().Cover(Noon, Noon.AddMinutes(10), [At(Noon.AddMinutes(10))]), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon, Noon.AddMinutes(10), [At(Noon.AddMinutes(-5))]), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon, Noon.AddMinutes(10), [At(Noon.AddMinutes(1))]), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon, Noon.AddMinutes(10), [At(Noon, Eth)]), Throws.ArgumentException);
            Assert.That(() => OpenInterestSeries.Create(Btc, Noon, Noon.AddMinutes(10), [At(Noon.AddMinutes(10))]), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_range_apart_from_the_coverage_is_rejected_newer_or_older()
    {
        Assert.Multiple(() =>
        {
            // Either would leave a hole inside the coverage.
            Assert.That(() => Covering().Cover(Noon.AddMinutes(5), Noon.AddMinutes(10), []), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon.AddHours(-3), Noon.AddHours(-2), []), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_range_must_end_after_it_starts_and_have_both_ends_where_a_step_starts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => OpenInterestSeries.Create(Btc, Noon, Noon, []), Throws.ArgumentException);
            Assert.That(() => OpenInterestSeries.Create(Btc, Noon.AddMinutes(1), Noon.AddMinutes(10), []), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon, Noon.AddMinutes(7), []), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon.AddMinutes(5), Noon, []), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_range_that_ends_where_the_coverage_starts_widens_it_back()
    {
        OpenInterestSeries series = Covering();

        series.Cover(Noon.AddHours(-2), Noon.AddHours(-1), []);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddHours(-2)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon));
            Assert.That(series.NewPoints, Is.Empty);
        });
    }

    [Test]
    public void A_range_that_starts_where_the_coverage_ends_widens_it_forward()
    {
        OpenInterestSeries series = Covering();

        series.Cover(Noon, Noon.AddMinutes(10), [At(Noon), At(Noon.AddMinutes(5))]);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddHours(-1)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon.AddMinutes(10)));
            Assert.That(series.NewPoints, Is.EqualTo(new[] { At(Noon), At(Noon.AddMinutes(5)) }));
        });
    }

    [Test]
    public void A_series_read_from_storage_has_no_points_to_save()
    {
        Assert.That(Covering().NewPoints, Is.Empty);
    }

    [Test]
    public void A_step_starts_every_five_minutes_of_the_hour()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenInterestSeries.StartsStep(Noon), Is.True);
            Assert.That(OpenInterestSeries.StartsStep(Noon.AddMinutes(55)), Is.True);
            Assert.That(OpenInterestSeries.StartsStep(Noon.AddMinutes(1)), Is.False);
            Assert.That(OpenInterestSeries.StartsStep(Noon.AddSeconds(1)), Is.False);

            // The same moment told in another time zone.
            Assert.That(OpenInterestSeries.StartsStep(Noon.ToOffset(TimeSpan.FromHours(7))), Is.True);
        });
    }

    [Test]
    public void The_start_of_a_step_is_the_five_minute_boundary_at_or_before_the_time_in_UTC()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenInterestSeries.StartOfStep(Noon), Is.EqualTo(Noon));
            Assert.That(OpenInterestSeries.StartOfStep(Noon.AddMinutes(4).AddSeconds(59)), Is.EqualTo(Noon));
            Assert.That(OpenInterestSeries.StartOfStep(Noon.AddMinutes(5)), Is.EqualTo(Noon.AddMinutes(5)));
            Assert.That(OpenInterestSeries.StartOfStep(Noon.AddMinutes(7).ToOffset(TimeSpan.FromHours(7))), Is.EqualTo(Noon.AddMinutes(5)));
        });
    }

    [Test]
    public void The_steps_of_a_range_are_counted_its_end_left_out()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenInterestSeries.StepsIn(Noon, Noon.AddMinutes(5)), Is.EqualTo(1));
            Assert.That(OpenInterestSeries.StepsIn(Noon, Noon.AddMinutes(1000)), Is.EqualTo(200));
            Assert.That(OpenInterestSeries.StepsIn(Noon, Noon), Is.Zero);
        });
    }
}