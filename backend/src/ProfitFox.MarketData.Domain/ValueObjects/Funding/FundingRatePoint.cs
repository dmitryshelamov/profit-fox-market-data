namespace ProfitFox.MarketData.Domain.ValueObjects.Funding;

/// <summary>
///     The funding rate of the coming payment of an instrument as its exchange told it at <see cref="Time" />. The exchange keeps changing
///     the rate until the payment, so the points of an instrument are a series of their own, each with its own time; a time without a
///     point was not collected.
/// </summary>
public sealed record FundingRatePoint
{
    /// <summary>
    ///     The id of the instrument in the catalog the point is of.
    /// </summary>
    public Guid InstrumentId { get; }

    public DateTimeOffset Time { get; }

    /// <summary>
    ///     The rate as a fraction, not a percent: 0.0001 is 0.01%. Below zero when the shorts pay the longs.
    /// </summary>
    public decimal Rate { get; }

    public FundingRatePoint(Guid instrumentId, DateTimeOffset time, decimal rate)
    {
        InstrumentId = instrumentId;
        Time = time;
        Rate = rate;
    }
}