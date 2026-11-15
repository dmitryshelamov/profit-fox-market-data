namespace ProfitFox.MarketData.Application.Queries;

/// <summary>
///     What a read of a series of points asks for, the open interest, the funding rates, the funding payments and the minutes of
///     liquidations alike: the stored points in <c>[From, To)</c>, at most <see cref="MaxPoints" /> of them an answer.
/// </summary>
public sealed record PointWindow
{
    public const int MaxPoints = 1000;

    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }

    /// <exception cref="ArgumentException">The window does not end after it starts.</exception>
    public PointWindow(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentException("The window must end after it starts.");
        }

        From = from;
        To = to;
    }
}