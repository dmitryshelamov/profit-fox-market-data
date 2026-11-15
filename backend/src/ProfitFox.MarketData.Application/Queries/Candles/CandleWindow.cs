using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     What a read of candles asks for: the whole candles of <see cref="Interval" /> in <see cref="Range" />, at most
///     <see cref="MaxCandles" /> of them.
/// </summary>
public sealed record CandleWindow
{
    public const int MaxCandles = 1000;

    public CandleInterval Interval { get; }

    public MinuteRange Range { get; }

    /// <exception cref="ArgumentException">
    ///     The window does not end after it starts, an end is not where a candle of the interval starts, or it holds more than
    ///     <see cref="MaxCandles" /> candles.
    /// </exception>
    public CandleWindow(CandleInterval interval, DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentException("The window must end after it starts.");
        }

        if (!interval.StartsAt(from) || !interval.StartsAt(to))
        {
            throw new ArgumentException("The window must start and end where candles of the interval start, in UTC.");
        }

        if ((to - from) / interval.Length() > MaxCandles)
        {
            throw new ArgumentException($"A window holds at most {MaxCandles} candles.");
        }

        Interval = interval;
        Range = new MinuteRange(from, to);
    }
}