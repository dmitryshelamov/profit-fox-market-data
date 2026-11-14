namespace ProfitFox.MarketData.Domain.ValueObjects.Candles;

/// <summary>
///     The whole minutes <c>[From, Until)</c>: both ends on the start of a minute, so a range never splits the minute of a candle.
/// </summary>
public sealed record MinuteRange
{
    public DateTimeOffset From { get; }

    public DateTimeOffset Until { get; }

    /// <exception cref="ArgumentException">An end is inside a minute, or the range does not end after it starts.</exception>
    public MinuteRange(DateTimeOffset from, DateTimeOffset until)
    {
        EnsureStartOfMinute(from, nameof(from));
        EnsureStartOfMinute(until, nameof(until));

        if (until <= from)
        {
            throw new ArgumentException($"A range of minutes must end after it starts, not at {until:O} from {from:O}.", nameof(until));
        }

        From = from;
        Until = until;
    }

    /// <summary>
    ///     The parts of this range outside <paramref name="other" />, oldest first: the whole range when the two are apart, none when
    ///     <paramref name="other" /> holds all of it, else the part before it, the part after it or both.
    /// </summary>
    public IReadOnlyList<MinuteRange> Except(MinuteRange other)
    {
        if (other.Until <= From || other.From >= Until)
        {
            return [this];
        }

        var parts = new List<MinuteRange>();

        if (From < other.From)
        {
            parts.Add(new MinuteRange(From, other.From));
        }

        if (other.Until < Until)
        {
            parts.Add(new MinuteRange(other.Until, Until));
        }

        return parts;
    }

    private static void EnsureStartOfMinute(DateTimeOffset time, string name)
    {
        if (time.UtcTicks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentException($"A range of minutes ends on the start of a minute, not at {time:O}.", name);
        }
    }
}