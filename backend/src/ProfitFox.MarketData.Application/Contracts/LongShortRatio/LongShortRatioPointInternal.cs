namespace ProfitFox.MarketData.Application.Contracts.LongShortRatio;

/// <summary>
///     The long/short ratio of an instrument at a time, as it is stored: the shares of the accounts holding it that were net long and
///     net short, fractions.
/// </summary>
public sealed record LongShortRatioPointInternal(DateTimeOffset Time, decimal Long, decimal Short);