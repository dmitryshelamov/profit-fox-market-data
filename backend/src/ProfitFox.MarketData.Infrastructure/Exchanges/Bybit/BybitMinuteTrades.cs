using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     What the trades of the instruments of one subscription tell of their minute candles. It sums the trades by the minute they were
///     made in and keeps, for every symbol, the sums of the minute that goes on and the candle and the sums of the last closed one. From
///     them come the buy volume of a closed candle, which counts a trade that comes after its candle, and the forming candle of the
///     minute that goes on. Trades and candles come on connections of their own, so every call is safe from any thread.
/// </summary>
internal sealed class BybitMinuteTrades
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new Lock();

    private readonly Dictionary<string, Symbol> _symbols = [];

    // The minutes trades were counted for since the forming candles were last taken.
    private readonly HashSet<(string Symbol, DateTimeOffset Minute)> _changed = [];

    /// <summary>
    ///     The trades of the symbols are seen from <paramref name="since" /> on: when their subscription is in place, and again when its
    ///     connection is back. A forming candle is told only for a minute that began at or after that, so one seen in part never passes
    ///     for a whole one.
    /// </summary>
    public void Watch(IEnumerable<string> symbols, DateTimeOffset since)
    {
        DateTimeOffset start = MinuteRange.StartOfMinute(since);
        DateTimeOffset firstWhole = start == since ? start : start + Minute;

        lock (_lock)
        {
            foreach (string symbol in symbols)
            {
                Of(symbol).WatchedFrom = firstWhole;
            }
        }
    }

    /// <summary>
    ///     Counts a trade of <paramref name="quantity" /> at <paramref name="price" /> made at <paramref name="time" />;
    ///     <paramref name="bought" /> when the buyer started it. A trade of the last closed minute that makes the trades of that minute
    ///     add up to the volume of its candle gives the candle again, now with its buy volume; any other trade gives null. A trade of a
    ///     minute before the last closed one is not counted.
    /// </summary>
    public Candle? Add(string symbol, DateTimeOffset time, decimal quantity, decimal price, bool bought)
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
                state.Minutes[minute] = sums = new Sums(price);
            }

            sums.Count(quantity, price, bought);
            _changed.Add((symbol, minute));

            if (state.Closed is not { } late || late.OpenTime != minute)
            {
                return null;
            }

            // The kept candle is as Bybit told it, without a buy volume, so it has one only once the trades add up.
            Candle completed = late.WithBuyVolume(sums.Volume);

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

                // The forming candle of the next minute opens at the close of this one, which is known only now.
                if (state.Minutes.ContainsKey(candle.OpenTime + Minute))
                {
                    _changed.Add((symbol, candle.OpenTime + Minute));
                }
            }

            state.Minutes.TryGetValue(candle.OpenTime, out Sums? sums);

            // A minute nothing is counted for adds up only to no volume at all.
            return candle.WithBuyVolume(sums?.Volume ?? TradedVolume.None);
        }
    }

    /// <summary>
    ///     The forming candles of the minutes that go on and had a trade counted since the last call, each built from the trades of its
    ///     minute alone, so its volume, turnover and buy volume agree with one another. It opens at the close of the minute before, as a
    ///     candle of Bybit does, or at its first trade while that close is not known yet. A minute not watched from its start, or closed
    ///     already, gives none.
    /// </summary>
    public IReadOnlyList<(string Symbol, Candle Candle)> TakeForming()
    {
        lock (_lock)
        {
            var forming = new List<(string Symbol, Candle Candle)>(_changed.Count);

            foreach ((string symbol, DateTimeOffset minute) in _changed)
            {
                Symbol state = _symbols[symbol];

                if (state.Minutes.TryGetValue(minute, out Sums? sums)
                    && (state.Closed is null || minute > state.Closed.OpenTime)
                    && state.WatchedFrom is { } watchedFrom
                    && minute >= watchedFrom)
                {
                    forming.Add((symbol, Forming(minute, sums, state.Closed)));
                }
            }

            _changed.Clear();

            return forming;
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

    private static Candle Forming(DateTimeOffset minute, Sums sums, Candle? closed)
    {
        decimal open = closed is not null && closed.OpenTime == minute - Minute ? closed.Close : sums.First;

        return new Candle(
            minute,
            open,
            Math.Max(open, sums.High),
            Math.Min(open, sums.Low),
            sums.Last,
            sums.Volume.Traded,
            sums.Turnover,
            sums.Volume.Bought);
    }

    private sealed class Symbol
    {
        public Dictionary<DateTimeOffset, Sums> Minutes { get; } = [];

        public Candle? Closed { get; set; }

        public DateTimeOffset? WatchedFrom { get; set; }
    }

    // The trades of one minute of a symbol; it starts with its first trade.
    private sealed class Sums(decimal firstPrice)
    {
        public decimal First { get; } = firstPrice;

        public decimal High { get; private set; } = firstPrice;

        public decimal Low { get; private set; } = firstPrice;

        public decimal Last { get; private set; } = firstPrice;

        public TradedVolume Volume { get; private set; } = TradedVolume.None;

        public decimal Turnover { get; private set; }

        public void Count(decimal quantity, decimal price, bool bought)
        {
            High = Math.Max(High, price);
            Low = Math.Min(Low, price);
            Last = price;
            Volume = Volume.Add(quantity, bought);
            Turnover += quantity * price;
        }
    }
}