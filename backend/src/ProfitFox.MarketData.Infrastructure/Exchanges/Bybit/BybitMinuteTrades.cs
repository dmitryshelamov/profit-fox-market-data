using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Tells how much of the volume of a closed candle the buyers took, from the trades of the instruments of one subscription. It sums
///     the trades by the minute they were made in and keeps, for every symbol, the sums of the minute that goes on and the candle and
///     the sums of the last closed one, so a trade of that minute that comes after its candle still counts. Trades and candles come on
///     connections of their own, so every call is safe from any thread.
/// </summary>
internal sealed class BybitMinuteTrades
{
    private readonly Lock _lock = new Lock();

    private readonly Dictionary<string, Symbol> _symbols = [];

    /// <summary>
    ///     Counts a trade of <paramref name="quantity" /> made at <paramref name="time" />; <paramref name="bought" /> when the buyer
    ///     started it. A trade of the last closed minute that makes the trades of that minute add up to the volume of its candle gives
    ///     the candle again, now with its buy volume; any other trade gives null. A trade of a minute before the last closed one is not
    ///     counted.
    /// </summary>
    public Candle? Add(string symbol, DateTimeOffset time, decimal quantity, bool bought)
    {
        DateTimeOffset minute = MinuteRange.StartOfMinute(time);

        lock (_lock)
        {
            Symbol state = Of(symbol);

            if (state.Closed is { } closed && minute < closed.OpenTime)
            {
                return null;
            }

            if (!state.Minutes.TryGetValue(minute, out Sums? sums))
            {
                state.Minutes[minute] = sums = new Sums();
            }

            sums.Traded += quantity;
            sums.Bought += bought ? quantity : 0m;

            return state.Closed is { } late && late.OpenTime == minute && sums.Traded == late.Volume ? WithBuyVolume(late, sums.Bought) : null;
        }
    }

    /// <summary>
    ///     The closed <paramref name="candle" /> as Bybit told it, with its buy volume when the trades counted for its minute add up to
    ///     its volume; when they do not, some of them were not seen and the candle stays without one. The candle is kept until the next
    ///     minute of the symbol closes; the sums of the minutes before it are forgotten.
    /// </summary>
    public Candle Close(string symbol, Candle candle)
    {
        lock (_lock)
        {
            Symbol state = Of(symbol);

            // A candle older than the one kept, as after a reconnect, takes what is still counted for it and is not kept.
            if (state.Closed is null || candle.OpenTime >= state.Closed.OpenTime)
            {
                state.Closed = candle;

                foreach (DateTimeOffset before in state.Minutes.Keys.Where(counted => counted < candle.OpenTime).ToArray())
                {
                    state.Minutes.Remove(before);
                }
            }

            state.Minutes.TryGetValue(candle.OpenTime, out Sums? sums);

            // A minute nothing is counted for adds up only to no volume at all.
            return (sums?.Traded ?? 0m) == candle.Volume ? WithBuyVolume(candle, sums?.Bought ?? 0m) : candle;
        }
    }

    private Symbol Of(string symbol)
    {
        if (!_symbols.TryGetValue(symbol, out Symbol? state))
        {
            _symbols[symbol] = state = new Symbol();
        }

        return state;
    }

    private static Candle WithBuyVolume(Candle candle, decimal buyVolume)
    {
        return new Candle(candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.Turnover, buyVolume);
    }

    private sealed class Symbol
    {
        public Dictionary<DateTimeOffset, Sums> Minutes { get; } = [];

        public Candle? Closed { get; set; }
    }

    private sealed class Sums
    {
        public decimal Traded { get; set; }

        public decimal Bought { get; set; }
    }
}