namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     The intervals the API builds candles of from the stored minutes, in UTC: a day starts at midnight, four hours at 00, 04, 08 and so on.
/// </summary>
public enum CandleInterval
{
    OneMinute,
    FiveMinutes,
    FifteenMinutes,
    OneHour,
    FourHours,
    OneDay
}