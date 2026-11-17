using ProfitFox.MarketData.Application.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     A UTC day a read of history reads with one statement, with the parts of the coverage inside it as the arrays the statement takes:
///     the ranges of every instrument apart, one entry a range.
/// </summary>
internal sealed record HistoryDay
{
    public DateTimeOffset From { get; }

    public DateTimeOffset Until { get; }

    public Guid[] InstrumentIds { get; }

    public DateTimeOffset[] Froms { get; }

    public DateTimeOffset[] Untils { get; }

    private HistoryDay(DateTimeOffset from, IReadOnlyList<(Guid InstrumentId, DateTimeOffset From, DateTimeOffset Until)> ranges)
    {
        From = from;
        Until = from.AddDays(1);
        InstrumentIds = [.. ranges.Select(range => range.InstrumentId)];
        Froms = [.. ranges.Select(range => range.From)];
        Untils = [.. ranges.Select(range => range.Until)];
    }

    /// <summary>
    ///     The UTC days the coverage touches, oldest first, each with the coverage cut to it; a day nothing covers is left out. The days
    ///     are made as they are asked for, so a read of years holds one of them at a time.
    /// </summary>
    public static IEnumerable<HistoryDay> Split(IReadOnlyList<InstrumentCoverageInternal> coverage)
    {
        IReadOnlyList<(Guid InstrumentId, TimeRangeInternal Range)> ranges =
        [
            .. coverage.SelectMany(instrument => instrument.Ranges.Select(range => (instrument.InstrumentId, range)))
        ];

        if (ranges.Count == 0)
        {
            yield break;
        }

        DateTimeOffset first = StartOfDay(ranges.Min(item => item.Range.From));
        DateTimeOffset last = ranges.Max(item => item.Range.Until);

        for (DateTimeOffset day = first; day < last; day = day.AddDays(1))
        {
            DateTimeOffset end = day.AddDays(1);
            List<(Guid InstrumentId, DateTimeOffset From, DateTimeOffset Until)> inside =
            [
                .. ranges
                    .Where(item => item.Range.From < end && item.Range.Until > day)
                    .Select(item => (item.InstrumentId, item.Range.From > day ? item.Range.From : day, item.Range.Until < end ? item.Range.Until : end))
            ];

            if (inside.Count > 0)
            {
                yield return new HistoryDay(day, inside);
            }
        }
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero);
    }
}