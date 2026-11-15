using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Tests.Unit.Domain.Entities.Funding;

[TestFixture]
public class FundingPaymentSeriesTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static FundingPayment PaidAt(DateTimeOffset time)
    {
        return new FundingPayment(time, rate: 0.0001m);
    }

    // The series that covers [08:00, 12:00) of the day.
    private static FundingPaymentSeries Covering()
    {
        return FundingPaymentSeries.Restore(Btc, Noon.AddHours(-4), Noon);
    }

    [Test]
    public void A_new_series_covers_its_first_range_and_keeps_the_payments_of_it_to_save()
    {
        FundingPayment[] payments = [PaidAt(Noon.AddHours(-8)), PaidAt(Noon.AddHours(-1))];

        var series = FundingPaymentSeries.Create(Btc, Noon.AddHours(-8), Noon, payments);

        Assert.Multiple(() =>
        {
            Assert.That(series.InstrumentId, Is.EqualTo(Btc));
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddHours(-8)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon));
            Assert.That(series.NewPayments, Is.EqualTo(payments));
        });
    }

    [Test]
    public void A_newer_range_apart_from_the_coverage_takes_its_place()
    {
        FundingPaymentSeries series = Covering();

        series.Cover(Noon.AddHours(8), Noon.AddHours(16), [PaidAt(Noon.AddHours(8))]);

        Assert.Multiple(() =>
        {
            // The time between is not known to be whole, so the coverage starts again at the new range.
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddHours(8)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon.AddHours(16)));
        });
    }

    [Test]
    public void A_payment_at_the_end_of_a_range_is_outside_it()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FundingPaymentSeries.Create(Btc, Noon.AddHours(-8), Noon, [PaidAt(Noon)]), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon, Noon.AddHours(8), [PaidAt(Noon.AddHours(8))]), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon, Noon.AddHours(8), [PaidAt(Noon.AddSeconds(-1))]), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_range_that_does_not_end_after_it_starts_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FundingPaymentSeries.Create(Btc, Noon, Noon, []), Throws.ArgumentException);
            Assert.That(() => Covering().Cover(Noon.AddHours(1), Noon, []), Throws.ArgumentException);
        });
    }

    [Test]
    public void A_range_that_starts_where_the_coverage_ends_widens_it_forward()
    {
        FundingPaymentSeries series = Covering();

        series.Cover(Noon, Noon.AddHours(1), [PaidAt(Noon)]);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddHours(-4)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon.AddHours(1)));
            Assert.That(series.NewPayments, Is.EqualTo(new[] { PaidAt(Noon) }));
        });
    }

    [Test]
    public void A_range_that_ends_where_the_coverage_starts_widens_it_back()
    {
        FundingPaymentSeries series = Covering();

        series.Cover(Noon.AddDays(-30), Noon.AddHours(-4), []);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddDays(-30)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon));
            Assert.That(series.NewPayments, Is.Empty);
        });
    }

    [Test]
    public void A_range_within_the_coverage_leaves_it_as_it_is()
    {
        FundingPaymentSeries series = Covering();

        series.Cover(Noon.AddHours(-3), Noon.AddHours(-2), []);

        Assert.Multiple(() =>
        {
            Assert.That(series.CoveredFrom, Is.EqualTo(Noon.AddHours(-4)));
            Assert.That(series.CoveredUntil, Is.EqualTo(Noon));
        });
    }

    [Test]
    public void A_series_read_from_storage_has_no_payments_to_save()
    {
        Assert.That(Covering().NewPayments, Is.Empty);
    }

    [Test]
    public void An_older_range_apart_from_the_coverage_is_rejected()
    {
        Assert.That(() => Covering().Cover(Noon.AddDays(-2), Noon.AddDays(-1), []), Throws.ArgumentException);
    }
}