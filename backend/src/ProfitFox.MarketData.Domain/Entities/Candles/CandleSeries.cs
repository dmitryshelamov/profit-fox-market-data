using ProfitFox.MarketData.Domain.Entities.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Domain.Entities.Candles;

/// <summary>
///     The minute candles of one instrument of the catalog as far as storage holds them: inside every range of <see cref="Covered" />
///     every candle the exchange gave is stored, so a minute without a candle there had no trades (a quiet minute, a night, a weekend,
///     the time before the instrument started to trade) and is never asked for again. The coverage only grows, and only together with
///     the candles of the range it takes. Storage writes <see cref="NewCandles" /> with the coverage in one transaction, so the coverage
///     never claims a minute storage does not hold. Apart from that the series tells where the buy volumes of its candles are
///     <see cref="Settled" />: looked for in the trades of the exchange once, and not again.
/// </summary>
public sealed class CandleSeries : AggregateRoot
{
    private readonly List<MinuteRange> _covered = [];

    private readonly List<MinuteRange> _settled = [];

    private readonly List<Candle> _newCandles = [];

    /// <summary>
    ///     The id of the instrument in the catalog the candles are of; one series per instrument.
    /// </summary>
    public Guid InstrumentId { get; }

    /// <summary>
    ///     The covered ranges, oldest first, apart from one another: ranges that touch or overlap are joined into one.
    /// </summary>
    public IReadOnlyList<MinuteRange> Covered => _covered.AsReadOnly();

    /// <summary>
    ///     The covered ranges whose candles have every buy volume the trades of the exchange could tell, oldest first and apart from one
    ///     another: a candle without a buy volume there stays without one, and the trades of the range are not asked for again.
    /// </summary>
    public IReadOnlyList<MinuteRange> Settled => _settled.AsReadOnly();

    /// <summary>
    ///     The candles of the ranges taken since the series was read from storage, which storage writes together with the coverage.
    /// </summary>
    public IReadOnlyCollection<Candle> NewCandles => _newCandles.AsReadOnly();

    private CandleSeries(Guid instrumentId)
    {
        InstrumentId = instrumentId;
    }

    /// <summary>
    ///     The series of an instrument none of whose minutes is loaded yet.
    /// </summary>
    public static CandleSeries Create(Guid instrumentId)
    {
        return new CandleSeries(instrumentId);
    }

    /// <summary>
    ///     The series as storage holds it, with no new candles: <paramref name="covered" /> and <paramref name="settled" /> each apart
    ///     from one another and oldest first, as they were saved.
    /// </summary>
    public static CandleSeries Restore(Guid instrumentId, IEnumerable<MinuteRange> covered, IEnumerable<MinuteRange> settled)
    {
        var series = new CandleSeries(instrumentId);
        series._covered.AddRange(covered);
        series._settled.AddRange(settled);

        return series;
    }

    /// <summary>
    ///     Takes a loaded range with the candles the exchange gave for it, an empty list for a range without trades.
    /// </summary>
    /// <exception cref="ArgumentException">A candle opens outside the range.</exception>
    public void Cover(MinuteRange range, IReadOnlyCollection<Candle> candles)
    {
        // A candle outside the range would be stored without a coverage over it, or would stand for a minute the range was not asked for.
        foreach (Candle candle in candles)
        {
            if (candle.OpenTime < range.From || candle.OpenTime >= range.Until)
            {
                throw new ArgumentException(
                    $"A candle that opens at {candle.OpenTime:O} is outside the range [{range.From:O}, {range.Until:O}).",
                    nameof(candles));
            }
        }

        Take(_covered, range);
        _newCandles.AddRange(candles);
    }

    /// <summary>
    ///     Takes a covered range as one whose buy volumes are looked for: its candles that have none stay so.
    /// </summary>
    /// <exception cref="ArgumentException">A part of the range is not covered: candles loaded there later would never get a buy volume.</exception>
    public void Settle(MinuteRange range)
    {
        if (Uncovered(range).Count > 0)
        {
            throw new ArgumentException($"The range [{range.From:O}, {range.Until:O}) is not all covered, so its buy volumes cannot be settled.", nameof(range));
        }

        Take(_settled, range);
    }

    /// <summary>
    ///     The parts of <paramref name="range" /> no covered range holds, oldest first; none when all of it is covered.
    /// </summary>
    public IReadOnlyList<MinuteRange> Uncovered(MinuteRange range)
    {
        return Outside(_covered, range);
    }

    /// <summary>
    ///     The parts of <paramref name="range" /> no settled range holds, oldest first; none when all of it is settled.
    /// </summary>
    public IReadOnlyList<MinuteRange> Unsettled(MinuteRange range)
    {
        return Outside(_settled, range);
    }

    // The ranges the new one touches become one range with it; the others stay as they are, oldest first.
    private static void Take(List<MinuteRange> ranges, MinuteRange range)
    {
        MinuteRange joined = ranges.Where(range.Touches).Aggregate(range, (whole, taken) => whole.Join(taken));

        ranges.RemoveAll(range.Touches);
        ranges.Add(joined);
        ranges.Sort((left, right) => left.From.CompareTo(right.From));
    }

    private static IReadOnlyList<MinuteRange> Outside(List<MinuteRange> ranges, MinuteRange range)
    {
        IReadOnlyList<MinuteRange> outside = [range];

        foreach (MinuteRange taken in ranges)
        {
            outside = [.. outside.SelectMany(part => part.Except(taken))];
        }

        return outside;
    }
}