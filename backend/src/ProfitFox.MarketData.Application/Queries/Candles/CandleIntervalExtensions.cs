namespace ProfitFox.MarketData.Application.Queries.Candles;

public static class CandleIntervalExtensions
{
    public static TimeSpan Length(this CandleInterval interval)
    {
        return interval switch
        {
            CandleInterval.OneMinute => TimeSpan.FromMinutes(1),
            CandleInterval.FiveMinutes => TimeSpan.FromMinutes(5),
            CandleInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            CandleInterval.OneHour => TimeSpan.FromHours(1),
            CandleInterval.FourHours => TimeSpan.FromHours(4),
            CandleInterval.OneDay => TimeSpan.FromDays(1),
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "The API builds no candles of this interval.")
        };
    }

    /// <summary>
    ///     Whether a candle of the interval starts at <paramref name="time" />: the candles of every interval start at whole multiples of it
    ///     from the Unix epoch, a midnight.
    /// </summary>
    public static bool StartsAt(this CandleInterval interval, DateTimeOffset time)
    {
        return (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % interval.Length().Ticks == 0;
    }
}