namespace ProfitFox.MarketData.Application.Contracts.Liquidations;

/// <summary>
///     The liquidations of an instrument over the minute that starts at <see cref="Time" />, as they are stored: what the feed of
///     liquidations tells, of the whole market, so it names its instrument. The sums are all that was summed of the minute so far, not
///     what the last save added.
/// </summary>
public sealed record InstrumentLiquidationMinuteInternal(
    Guid InstrumentId,
    DateTimeOffset Time,
    LiquidationSumInternal Longs,
    LiquidationSumInternal Shorts,
    bool IsValid);