namespace ProfitFox.MarketData.Domain.ValueObjects.Funding;

/// <summary>
///     A funding payment of an instrument: the rate its exchange settled at <see cref="Time" />, by which the holders of one side paid
///     those of the other. Unlike a <see cref="FundingRatePoint" />, which is the rate of a payment still to come, it no longer changes.
/// </summary>
public sealed record FundingPayment
{
    public DateTimeOffset Time { get; }

    /// <summary>
    ///     The rate as a fraction, not a percent: 0.0001 is 0.01%. Below zero when the shorts paid the longs.
    /// </summary>
    public decimal Rate { get; }

    public FundingPayment(DateTimeOffset time, decimal rate)
    {
        Time = time;
        Rate = rate;
    }
}