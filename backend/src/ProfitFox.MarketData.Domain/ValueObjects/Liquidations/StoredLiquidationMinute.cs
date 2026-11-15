namespace ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

/// <summary>
///     The liquidations of a minute of an instrument as storage holds them after a change: all that was summed of the minute so far,
///     and whether it is valid. Not valid when the liquidations of the minute were not all heard: the sums then tell only what was
///     heard, down to nothing.
/// </summary>
public sealed record StoredLiquidationMinute
{
    public LiquidationMinute Minute { get; }

    public bool IsValid { get; }

    public StoredLiquidationMinute(LiquidationMinute minute, bool isValid)
    {
        Minute = minute;
        IsValid = isValid;
    }
}