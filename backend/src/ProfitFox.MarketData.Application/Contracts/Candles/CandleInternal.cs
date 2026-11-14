namespace ProfitFox.MarketData.Application.Contracts.Candles;

/// <summary>
///     A candle of an interval built from the stored minutes. The turnover is the one of the exchange where it tells it (Bybit), else an
///     estimate of every minute, its volume times the mean of its high, low and close (T-Invest). The buy volume is the part of the volume
///     traded in trades the buyer started, the rest was sold into; null unless it is known for every minute of the candle.
/// </summary>
public sealed record CandleInternal(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover,
    decimal? BuyVolume);