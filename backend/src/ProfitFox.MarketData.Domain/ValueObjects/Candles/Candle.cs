namespace ProfitFox.MarketData.Domain.ValueObjects.Candles;

/// <summary>
///     The trades of an instrument over one closed minute that opens at <see cref="OpenTime" />, as its exchange told them: the prices of
///     the first, highest, lowest and last trades, what was traded and for how much. Candles of an instrument belong to its
///     <see cref="Entities.Candles.CandleSeries" />.
/// </summary>
public sealed record Candle
{
    public DateTimeOffset OpenTime { get; }

    public decimal Open { get; }

    public decimal High { get; }

    public decimal Low { get; }

    public decimal Close { get; }

    /// <summary>
    ///     What was traded: the base asset on Bybit, shares on T-Invest (its lots times the lot the share had when the candle was loaded).
    /// </summary>
    public decimal Volume { get; }

    /// <summary>
    ///     What the trades were worth in the quote asset, the sum of price times quantity over them; Bybit tells it, T-Invest does not.
    /// </summary>
    public decimal? Turnover { get; }

    public Candle(DateTimeOffset openTime, decimal open, decimal high, decimal low, decimal close, decimal volume, decimal? turnover = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(open);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(high);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(low);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(close);
        ArgumentOutOfRangeException.ThrowIfNegative(volume);

        if (openTime != MinuteRange.StartOfMinute(openTime))
        {
            throw new ArgumentException($"A minute candle opens at the start of a minute, not at {openTime:O}.", nameof(openTime));
        }

        if (turnover is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(turnover.Value, nameof(turnover));
        }

        EnsurePricesWithinRange(open, high, low, close);

        OpenTime = openTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Turnover = turnover;
    }

    // Every trade of the minute is between its lowest and highest: a candle that says otherwise was broken on the way.
    private static void EnsurePricesWithinRange(decimal open, decimal high, decimal low, decimal close)
    {
        if (low > high)
        {
            throw new ArgumentException($"The low {low} of a candle is above its high {high}.", nameof(low));
        }

        if (open < low || open > high || close < low || close > high)
        {
            throw new ArgumentException($"The open {open} and the close {close} of a candle must lie between its low {low} and high {high}.");
        }
    }
}