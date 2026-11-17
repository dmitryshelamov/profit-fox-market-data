namespace ProfitFox.MarketData.Application.Queries;

/// <summary>
///     What a read of history asks for, of every kind of data alike: <c>[From, To)</c> of past days only, as a backtest reads them. The
///     current UTC day is never in it: the buy volumes of its minutes and its points may still change.
/// </summary>
public sealed record HistoryWindow
{
    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }

    /// <exception cref="ArgumentException">The window does not end after it starts, or ends after the start of the UTC day of <paramref name="now" />.</exception>
    public HistoryWindow(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        if (to <= from)
        {
            throw new ArgumentException("The window must end after it starts.");
        }

        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        if (to > today)
        {
            throw new ArgumentException($"The history holds past days only: the window must end at {today:O} or before.");
        }

        From = from;
        To = to;
    }
}