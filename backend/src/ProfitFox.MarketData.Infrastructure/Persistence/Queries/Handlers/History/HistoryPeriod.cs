using ProfitFox.MarketData.Application.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     A part of a read of history that one statement reads: whole UTC days in a row, with the parts of the coverage inside it as the
///     arrays the statement takes, the ranges of every instrument apart, one entry a range.
/// </summary>
internal sealed record HistoryPeriod
{
    /// <summary>
    ///     The most days times instruments a part holds, unless one day alone holds more: then the day is a part of its own. A read of one
    ///     instrument takes 64 days a statement and one of the market a day, as Postgres sorts and hashes it.
    /// </summary>
    public const int MaxInstrumentDays = 64;

    public DateTimeOffset From { get; }

    public DateTimeOffset Until { get; }

    public Guid[] InstrumentIds { get; }

    public DateTimeOffset[] Froms { get; }

    public DateTimeOffset[] Untils { get; }

    private HistoryPeriod(DateTimeOffset from, DateTimeOffset until, IReadOnlyList<(Guid InstrumentId, DateTimeOffset From, DateTimeOffset Until)> ranges)
    {
        From = from;
        Until = until;
        InstrumentIds = [.. ranges.Select(range => range.InstrumentId)];
        Froms = [.. ranges.Select(range => range.From)];
        Untils = [.. ranges.Select(range => range.Until)];
    }

    /// <summary>
    ///     The parts of the coverage, oldest first: the UTC days it touches, joined in a row while the days of a part from its first to
    ///     its last times the instruments covered in them stay within <see cref="MaxInstrumentDays" />. A part starts and ends on a day
    ///     something covers, so a day nothing covers is read by no statement alone. The parts are made as they are asked for, so a read
    ///     of years holds one of them at a time.
    /// </summary>
    public static IEnumerable<HistoryPeriod> Split(IReadOnlyList<InstrumentCoverageInternal> coverage)
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
        DateTimeOffset? start = null;
        DateTimeOffset end = first;
        var instruments = new HashSet<Guid>();

        for (DateTimeOffset day = first; day < last; day = day.AddDays(1))
        {
            DateTimeOffset next = day.AddDays(1);
            Guid[] covered = [.. ranges.Where(item => item.Range.From < next && item.Range.Until > day).Select(item => item.InstrumentId).Distinct()];

            if (covered.Length == 0)
            {
                continue;
            }

            if (start is { } from && (next - from).Days * instruments.Union(covered).Count() > MaxInstrumentDays)
            {
                yield return Cut(ranges, from, end);
                start = null;
                instruments.Clear();
            }

            start ??= day;
            end = next;
            instruments.UnionWith(covered);
        }

        if (start is { } rest)
        {
            yield return Cut(ranges, rest, end);
        }
    }

    // The ranges inside [from, until), cut to it.
    private static HistoryPeriod Cut(IReadOnlyList<(Guid InstrumentId, TimeRangeInternal Range)> ranges, DateTimeOffset from, DateTimeOffset until)
    {
        return new HistoryPeriod(
            from,
            until,
            [
                .. ranges
                    .Where(item => item.Range.From < until && item.Range.Until > from)
                    .Select(item => (item.InstrumentId, item.Range.From > from ? item.Range.From : from, item.Range.Until < until ? item.Range.Until : until))
            ]);
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero);
    }
}