namespace ProfitFox.MarketData.Application.Contracts.Candles;

/// <summary>
///     A candle of an interval built from the stored minutes. The turnover is the one of the exchange where it tells it (Bybit), else an
///     estimate of every minute, its volume times the mean of its high, low and close (T-Invest).
/// </summary>
public sealed record CandleInternal(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover);