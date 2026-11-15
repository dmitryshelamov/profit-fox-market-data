namespace ProfitFox.MarketData.Application.Contracts.Liquidations;

/// <summary>
///     The liquidations of an instrument over the minute that starts at <see cref="Time" />, the liquidated longs and shorts apart,
///     as they are stored. Not valid when the liquidations of the minute were not all heard: the sums then tell only what was heard,
///     down to nothing. The minute that goes on is told with what is stored of it so far.
/// </summary>
public sealed record LiquidationMinuteInternal(DateTimeOffset Time, LiquidationSumInternal Longs, LiquidationSumInternal Shorts, bool IsValid);