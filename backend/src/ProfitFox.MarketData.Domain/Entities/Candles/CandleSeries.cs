using ProfitFox.MarketData.Domain.Entities.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Domain.Entities.Candles;

/// <summary>
///     The minute candles of one instrument of the catalog as far as storage holds them: inside <c>[CoveredFrom, CoveredUntil)</c> every
///     candle the exchange gave is stored, so a minute missing there had no trades (a quiet minute, a night, a weekend) and is never asked
///     for again. The coverage only grows, and only together with the candles of the range it takes: forward as new minutes close, back as
///     older history is loaded. Storage writes <see cref="NewCandles" /> with the coverage in one transaction, so the coverage never
///     claims a minute storage does not hold.
/// </summary>
public sealed class CandleSeries : AggregateRoot
{
    private readonly List<Candle> _newCandles = [];

    /// <summary>
    ///     The id of the instrument in the catalog the candles are of; one series per instrument.
    /// </summary>
    public Guid InstrumentId { get; }

    public DateTimeOffset CoveredFrom { get; private set; }

    public DateTimeOffset CoveredUntil { get; private set; }

    /// <summary>
    ///     The candles of the ranges taken since the series was read from storage, which storage writes together with the coverage.
    /// </summary>
    public IReadOnlyCollection<Candle> NewCandles => _newCandles.AsReadOnly();

    private CandleSeries(Guid instrumentId, DateTimeOffset coveredFrom, DateTimeOffset coveredUntil)
    {
        InstrumentId = instrumentId;
        CoveredFrom = coveredFrom;
        CoveredUntil = coveredUntil;
    }

    /// <summary>
    ///     The series of an instrument once its first range <c>[coveredFrom, coveredUntil)</c> is loaded with the candles the exchange gave
    ///     for it.
    /// </summary>
    /// <exception cref="ArgumentException">The range is empty, or a candle opens outside it.</exception>
    public static CandleSeries Create(Guid instrumentId, DateTimeOffset coveredFrom, DateTimeOffset coveredUntil, IReadOnlyCollection<Candle> candles)
    {
        EnsureLoadedRange(coveredFrom, coveredUntil, candles);

        var series = new CandleSeries(instrumentId, coveredFrom, coveredUntil);
        series._newCandles.AddRange(candles);

        return series;
    }

    /// <summary>
    ///     Takes a loaded range <c>[from, until)</c> with the candles the exchange gave for it, when the range touches or overlaps the
    ///     coverage. A range apart from the coverage is left out with its candles: the minutes between them are not loaded, and a coverage
    ///     over them would say they had no trades.
    /// </summary>
    /// <exception cref="ArgumentException">The range is empty, or a candle opens outside it.</exception>
    public void Cover(DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<Candle> candles)
    {
        EnsureLoadedRange(from, until, candles);

        if (until < CoveredFrom || from > CoveredUntil)
        {
            return;
        }

        CoveredFrom = from < CoveredFrom ? from : CoveredFrom;
        CoveredUntil = until > CoveredUntil ? until : CoveredUntil;
        _newCandles.AddRange(candles);
    }

    private static void EnsureLoadedRange(DateTimeOffset from, DateTimeOffset until, IReadOnlyCollection<Candle> candles)
    {
        if (until <= from)
        {
            throw new ArgumentException($"A covered range must end after it starts, not at {until:O} from {from:O}.", nameof(until));
        }

        // A candle outside the range would be stored without a coverage over it, or would stand for a minute the range was not asked for.
        foreach (Candle candle in candles)
        {
            if (candle.OpenTime < from || candle.OpenTime >= until)
            {
                throw new ArgumentException($"A candle that opens at {candle.OpenTime:O} is outside the range [{from:O}, {until:O}).", nameof(candles));
            }
        }
    }
}