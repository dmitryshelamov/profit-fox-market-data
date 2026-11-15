namespace ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;

/// <summary>
///     The long/short ratio of an instrument at <see cref="Time" />, as its exchange told it: of the accounts that held a position in
///     it then, the share that was net long and the share that was net short. It counts accounts, not the size of their positions.
///     The points of an instrument are a series of their own, one a step of the series; a step without a point is not loaded yet or
///     has none at the exchange.
/// </summary>
public sealed record LongShortRatioPoint
{
    /// <summary>
    ///     The id of the instrument in the catalog the point is of.
    /// </summary>
    public Guid InstrumentId { get; }

    public DateTimeOffset Time { get; }

    /// <summary>
    ///     The share of the accounts that were net long, a fraction: 0.73 for 73 of a hundred.
    /// </summary>
    public decimal Long { get; }

    /// <summary>
    ///     The share of the accounts that were net short, a fraction.
    /// </summary>
    public decimal Short { get; }

    public LongShortRatioPoint(Guid instrumentId, DateTimeOffset time, decimal longShare, decimal shortShare)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(longShare);
        ArgumentOutOfRangeException.ThrowIfNegative(shortShare);

        InstrumentId = instrumentId;
        Time = time;
        Long = longShare;
        Short = shortShare;
    }
}