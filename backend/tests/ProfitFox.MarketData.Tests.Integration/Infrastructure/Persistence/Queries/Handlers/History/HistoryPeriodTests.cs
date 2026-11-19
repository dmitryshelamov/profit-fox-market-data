using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     The parts a read of history splits its coverage into, one statement each. No database: the type is internal to the
///     infrastructure, which shows its internals only to this project.
/// </summary>
[TestFixture]
public class HistoryPeriodTests
{
    private static readonly DateTimeOffset Day = new DateTimeOffset(year: 2023, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static InstrumentCoverageInternal Covered(Guid instrumentId, params (DateTimeOffset From, DateTimeOffset Until)[] ranges)
    {
        return new InstrumentCoverageInternal(instrumentId, [.. ranges.Select(range => new TimeRangeInternal(range.From, range.Until))]);
    }

    private static InstrumentCoverageInternal[] Market(int instruments, DateTimeOffset from, DateTimeOffset until)
    {
        return [.. Enumerable.Range(0, instruments).Select(_ => Covered(Guid.NewGuid(), (from, until)))];
    }

    private static int[] Days(IEnumerable<HistoryPeriod> periods)
    {
        return [.. periods.Select(period => (int)(period.Until - period.From).TotalDays)];
    }

    [Test]
    public void One_instrument_of_927_days_is_read_in_parts_of_64_days_and_the_rest()
    {
        HistoryPeriod[] periods = [.. HistoryPeriod.Split(Market(1, Day, Day.AddDays(927)))];

        Assert.Multiple(() =>
        {
            Assert.That(Days(periods), Is.EqualTo(Enumerable.Repeat(64, 14).Append(31)));
            Assert.That(periods.Skip(1).Select(period => period.From), Is.EqualTo(periods.SkipLast(1).Select(period => period.Until)));
            Assert.That(periods[^1].Until, Is.EqualTo(Day.AddDays(927)));
        });
    }

    [Test]
    public void Ten_instruments_are_read_six_days_a_part()
    {
        Assert.That(Days(HistoryPeriod.Split(Market(10, Day, Day.AddDays(30)))), Is.EqualTo(Enumerable.Repeat(6, 5)));
    }

    [Test]
    public void A_market_of_65_instruments_is_read_a_day_a_part()
    {
        HistoryPeriod[] periods = [.. HistoryPeriod.Split(Market(65, Day, Day.AddDays(3)))];

        Assert.Multiple(() =>
        {
            Assert.That(Days(periods), Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(periods.Select(period => period.InstrumentIds.Length), Is.All.EqualTo(65));
        });
    }

    [Test]
    public void Days_nothing_covers_between_two_ranges_are_inside_one_part_and_the_ranges_stay_apart()
    {
        var instrument = Guid.NewGuid();

        HistoryPeriod period = HistoryPeriod.Split([Covered(instrument, (Day, Day.AddDays(3)), (Day.AddDays(10), Day.AddDays(12)))]).Single();

        Assert.Multiple(() =>
        {
            Assert.That((period.From, period.Until), Is.EqualTo((Day, Day.AddDays(12))));
            Assert.That(period.InstrumentIds, Is.EqualTo(new[] { instrument, instrument }));
            Assert.That(period.Froms, Is.EqualTo(new[] { Day, Day.AddDays(10) }));
            Assert.That(period.Untils, Is.EqualTo(new[] { Day.AddDays(3), Day.AddDays(12) }));
        });
    }

    [Test]
    public void A_part_ends_with_the_last_day_its_coverage_touches_and_cuts_the_ranges_to_itself()
    {
        var instrument = Guid.NewGuid();

        // 70 days from a quarter past ten: the first part ends after 64 days, the second at the end of the day the coverage ends in.
        HistoryPeriod[] periods = [.. HistoryPeriod.Split([Covered(instrument, (Day.AddHours(10.25), Day.AddDays(69).AddHours(6)))])];

        Assert.Multiple(() =>
        {
            Assert.That(periods.Select(period => (period.From, period.Until)), Is.EqualTo(new[] { (Day, Day.AddDays(64)), (Day.AddDays(64), Day.AddDays(70)) }));
            Assert.That(periods.Select(period => (period.Froms.Single(), period.Untils.Single())),
                Is.EqualTo(new[] { (Day.AddHours(10.25), Day.AddDays(64)), (Day.AddDays(64), Day.AddDays(69).AddHours(6)) }));
        });
    }

    [Test]
    public void A_coverage_of_no_range_gives_no_part()
    {
        Assert.That(HistoryPeriod.Split([Covered(Guid.NewGuid())]), Is.Empty);
    }
}