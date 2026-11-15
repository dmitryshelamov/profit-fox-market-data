namespace ProfitFox.MarketData.Application.Ports.LongShortRatio;

/// <summary>
///     The long/short ratio of a contract of Bybit at a time of its history: the shares of the accounts holding it that were net
///     long and net short, fractions.
/// </summary>
public sealed record BybitLongShortRatioPoint(DateTimeOffset Time, decimal Long, decimal Short);