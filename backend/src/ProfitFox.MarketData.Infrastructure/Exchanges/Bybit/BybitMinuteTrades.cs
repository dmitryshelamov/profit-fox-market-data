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

            TradedVolume trades = state.Minutes.GetValueOrDefault(minute, TradedVolume.None).Add(quantity, bought);
            state.Minutes[minute] = trades;

            if (state.Closed is not { } late || late.OpenTime != minute)
            {
                return null;
            }

            // The kept candle is as Bybit told it, without a buy volume, so it has one only once the trades add up.
            Candle completed = late.WithBuyVolume(trades);

            return completed.BuyVolume is null ? null : completed;
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

            // A minute nothing is counted for adds up only to no volume at all.
            return candle.WithBuyVolume(state.Minutes.GetValueOrDefault(candle.OpenTime, TradedVolume.None));
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

    private sealed class Symbol
    {
        public Dictionary<DateTimeOffset, TradedVolume> Minutes { get; } = [];

        public Candle? Closed { get; set; }
    }
}