using ProfitFox.MarketData.Domain.Entities.Base;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;

namespace ProfitFox.MarketData.Domain.Entities.LongShortRatio;

/// <summary>
///     The long/short ratio of one instrument of the catalog as far as storage holds it. The series has one point every
///     <see cref="Step" />, the finest step the history of an exchange keeps it at. From <see cref="CoveredFrom" /> up to
///     <see cref="CoveredUntil" /> every point the exchange has is stored, so a step without a point there has none (the time before
///     the instrument was listed) and is never asked for again. Storage writes <see cref="NewPoints" /> with the coverage in one
///     transaction, so the coverage never claims a time storage does not hold. The coverage is one stretch of time and grows only by a
///     range that touches it, so no hole hides inside it; a range apart from it is refused.
/// </summary>
public sealed class LongShortRatioSeries : AggregateRoot
{
    /// <summary>
    ///     The time between two points of a series.
    /// </summary>
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    private readonly List<LongShortRatioPoint> _newPoints = [];

    /// <summary>
    ///     The id of the instrument in the catalog the points are of; one series per instrument.
    /// </summary>
    public Guid InstrumentId { get; }

    public DateTimeOffset CoveredFrom { get; private set; }

    /// <summary>
    ///     The end of the coverage, itself outside it.
    /// </summary>
    public DateTimeOffset CoveredUntil { get; private set; }

    /// <summary>
    ///     The points of the ranges covered since the series was read from storage, which storage writes together with the coverage.
    /// </summary>
    public IReadOnlyCollection<LongShortRatioPoint> NewPoints => _newPoints.AsReadOnly();

    private LongShortRatioSeries(Guid instrumentId, DateTimeOffset coveredFrom, DateTimeOffset coveredUntil)
    {
        InstrumentId = instrumentId;
        CoveredFrom = coveredFrom;
        CoveredUntil = coveredUntil;
    }

    /// <summary>
    ///     The series of an instrument, started by the first range loaded for it: <c>[from, until)</c> with the points the exchange has
    ///     in it, an empty list for a range without points.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The range does not end after it starts, an end of it is not where a step starts, or a point is of another instrument, outside
    ///     the range or between two steps.
    /// </exception>
    public static LongShortRatioSeries Create(Guid instrumentId, DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<LongShortRatioPoint> points)
    {
        EnsureInside(instrumentId, from, until, points);

        var series = new LongShortRatioSeries(instrumentId, from, until);
        series._newPoints.AddRange(points);

        return series;
    }

    /// <summary>
    ///     The series as storage holds it, with no new points.
    /// </summary>
    public static LongShortRatioSeries Restore(Guid instrumentId, DateTimeOffset coveredFrom, DateTimeOffset coveredUntil)
    {
        return new LongShortRatioSeries(instrumentId, coveredFrom, coveredUntil);
    }

    /// <summary>
    ///     Whether a point of a series may be of the time: whether a step starts there.
    /// </summary>
    public static bool StartsStep(DateTimeOffset time)
    {
        return time.UtcTicks % Step.Ticks == 0;
    }

    /// <summary>
    ///     Where the step that holds the time starts.
    /// </summary>
    public static DateTimeOffset StartOfStep(DateTimeOffset time)
    {
        return new DateTimeOffset(time.UtcTicks - time.UtcTicks % Step.Ticks, TimeSpan.Zero);
    }

    /// <summary>
    ///     Takes a loaded range <c>[from, until)</c> that touches the coverage, with the points the exchange has in it, an empty list for
    ///     a range without points.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The range is apart from the coverage, does not end after it starts, an end of it is not where a step starts, or a point is of
    ///     another instrument, outside the range or between two steps.
    /// </exception>
    public void Cover(DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<LongShortRatioPoint> points)
    {
        EnsureInside(InstrumentId, from, until, points);

        if (from > CoveredUntil || until < CoveredFrom)
        {
            throw new ArgumentException(
                $"The range [{from:O}, {until:O}) is apart from the coverage [{CoveredFrom:O}, {CoveredUntil:O}).",
                nameof(from));
        }

        CoveredFrom = from < CoveredFrom ? from : CoveredFrom;
        CoveredUntil = until > CoveredUntil ? until : CoveredUntil;
        _newPoints.AddRange(points);
    }

    // A point outside the range would be stored without the coverage over it; one between two steps is of no step of the series.
    private static void EnsureInside(Guid instrumentId, DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<LongShortRatioPoint> points)
    {
        if (until <= from || !StartsStep(from) || !StartsStep(until))
        {
            throw new ArgumentException($"The range [{from:O}, {until:O}) must end after it starts, both where a step starts.", nameof(until));
        }

        foreach (LongShortRatioPoint point in points)
        {
            if (point.InstrumentId != instrumentId || point.Time < from || point.Time >= until || !StartsStep(point.Time))
            {
                throw new ArgumentException(
                    $"A point of {point.Time:O} of instrument {point.InstrumentId} is not a point of the range [{from:O}, {until:O}).",
                    nameof(points));
            }
        }
    }
}