namespace ProfitFox.MarketData.Application.Queries.OpenInterest;

/// <summary>
///     What a read of open interest asks for: the stored points in <c>[From, To)</c>, at most <see cref="MaxPoints" /> of them an
///     answer.
/// </summary>
public sealed record OpenInterestWindow
{
    public const int MaxPoints = 1000;

    public DateTimeOffset From { get; }

    public DateTimeOffset To { get; }

    /// <exception cref="ArgumentException">The window does not end after it starts.</exception>
    public OpenInterestWindow(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentException("The window must end after it starts.");
        }

        From = from;
        To = to;
    }
}