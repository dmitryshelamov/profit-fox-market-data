namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     What a read of the history of candles asks for: the whole candles of <see cref="Interval" /> in <see cref="Window" />, which
///     starts and ends where candles of the interval start.
/// </summary>
public sealed record CandleHistoryWindow
{
    public CandleInterval Interval { get; }

    public HistoryWindow Window { get; }

    /// <exception cref="ArgumentException">
    ///     The window does not end after it starts, ends after the start of the UTC day of <paramref name="now" />, or an end is not where a
    ///     candle of the interval starts.
    /// </exception>
    public CandleHistoryWindow(CandleInterval interval, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var window = new HistoryWindow(from, to, now);

        if (!interval.StartsAt(from) || !interval.StartsAt(to))
        {
            throw new ArgumentException("The window must start and end where candles of the interval start, in UTC.");
        }

        Interval = interval;
        Window = window;
    }
}