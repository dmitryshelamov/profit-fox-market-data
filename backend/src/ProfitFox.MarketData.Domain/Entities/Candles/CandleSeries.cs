using ProfitFox.MarketData.Domain.Entities.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Domain.Entities.Candles;

/// <summary>
///     The minute candles of one instrument of the catalog as far as storage holds them: inside every range of <see cref="Covered" />
///     every candle the exchange gave is stored, so a minute without a candle there had no trades (a quiet minute, a night, a weekend,
///     the time before the instrument started to trade) and is never asked for again. The coverage only grows, and only together with
///     the candles of the range it takes. Storage writes <see cref="NewCandles" /> with the coverage in one transaction, so the coverage
///     never claims a minute storage does not hold.
/// </summary>
public sealed class CandleSeries : AggregateRoot
{
    private readonly List<MinuteRange> _covered = [];

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

        DateTimeOffset from = range.From;
        DateTimeOffset until = range.Until;

        // The ranges that touch or overlap the new one are joined into it.
        _covered.RemoveAll(covered =>
        {
            if (covered.Until < from || covered.From > until)
            {
                return false;
            }

            from = covered.From < from ? covered.From : from;
            until = covered.Until > until ? covered.Until : until;

            return true;
        });

        int index = _covered.FindIndex(covered => covered.From > until);
        _covered.Insert(index < 0 ? _covered.Count : index, new MinuteRange(from, until));
        _newCandles.AddRange(candles);
    }

    /// <summary>
    ///     The parts of <paramref name="range" /> no covered range holds, oldest first; none when all of it is covered.
    /// </summary>
    public IReadOnlyList<MinuteRange> Uncovered(MinuteRange range)
    {
        IReadOnlyList<MinuteRange> uncovered = [range];

        foreach (MinuteRange covered in _covered)
        {
            uncovered = [.. uncovered.SelectMany(part => part.Except(covered))];
        }

        return uncovered;
    }
}