using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

/// <summary>
///     The liquidations of an instrument over the minute that starts at <see cref="Time" />, summed by the side of the liquidated
///     positions. The single liquidations are not kept. A minute nothing was liquidated in has no sums at all.
/// </summary>
public sealed record LiquidationMinute
{
    /// <summary>
    ///     The id of the instrument in the catalog the liquidations are of.
    /// </summary>
    public Guid InstrumentId { get; }

    public DateTimeOffset Time { get; }

    /// <summary>
    ///     The liquidated long positions.
    /// </summary>
    public LiquidationSum Longs { get; }

    /// <summary>
    ///     The liquidated short positions.
    /// </summary>
    public LiquidationSum Shorts { get; }

    public LiquidationMinute(Guid instrumentId, DateTimeOffset time, LiquidationSum longs, LiquidationSum shorts)
    {
        if (time != MinuteRange.StartOfMinute(time))
        {
            throw new ArgumentException($"The liquidations of a minute are summed from its start, not from {time:O}.", nameof(time));
        }

        InstrumentId = instrumentId;
        Time = time;
        Longs = longs;
        Shorts = shorts;
    }

    /// <summary>
    ///     The minute with one more liquidation of <paramref name="size" /> at <paramref name="price" /> counted;
    ///     <paramref name="ofLong" /> when the liquidated position was a long one.
    /// </summary>
    public LiquidationMinute Add(bool ofLong, decimal size, decimal price)
    {
        return ofLong
            ? new LiquidationMinute(InstrumentId, Time, Longs.Add(size, price), Shorts)
            : new LiquidationMinute(InstrumentId, Time, Longs, Shorts.Add(size, price));
    }
}