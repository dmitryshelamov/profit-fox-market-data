namespace ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

/// <summary>
///     The open interest of an instrument at <see cref="Time" />: how much of the base asset its open contracts hold, as its exchange told
///     it then. The points of an instrument are a series of their own, each with its own time; a time without a point was not collected.
/// </summary>
public sealed record OpenInterestPoint
{
    /// <summary>
    ///     The id of the instrument in the catalog the point is of.
    /// </summary>
    public Guid InstrumentId { get; }

    public DateTimeOffset Time { get; }

    /// <summary>
    ///     The open interest in the base asset: BTC of BTCUSDT.
    /// </summary>
    public decimal Value { get; }

    public OpenInterestPoint(Guid instrumentId, DateTimeOffset time, decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        InstrumentId = instrumentId;
        Time = time;
        Value = value;
    }
}