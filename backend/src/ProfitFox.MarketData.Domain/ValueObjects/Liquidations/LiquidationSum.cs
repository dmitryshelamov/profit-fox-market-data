namespace ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

/// <summary>
///     What the liquidated positions of one side of an instrument add up to over a minute, the longs or the shorts.
/// </summary>
public sealed record LiquidationSum
{
    /// <summary>
    ///     A minute no position of the side was liquidated in.
    /// </summary>
    public static LiquidationSum None { get; } = new LiquidationSum(volume: 0m, turnover: 0m, count: 0);

    /// <summary>
    ///     What was liquidated, in the base asset: BTC of BTCUSDT.
    /// </summary>
    public decimal Volume { get; }

    /// <summary>
    ///     What it was worth in the quote asset, the sum of size times price over the liquidations. The price an exchange tells is
    ///     the bankruptcy price of the position, not the one it was closed at, so this is close to what was really paid, not equal.
    /// </summary>
    public decimal Turnover { get; }

    /// <summary>
    ///     How many liquidations were summed: one large liquidation and a hundred small ones may have the same volume.
    /// </summary>
    public int Count { get; }

    public LiquidationSum(decimal volume, decimal turnover, int count)
    {
        Volume = volume;
        Turnover = turnover;
        Count = count;
    }

    /// <summary>
    ///     The sum with one more liquidation of <paramref name="size" /> at <paramref name="price" /> counted.
    /// </summary>
    public LiquidationSum Add(decimal size, decimal price)
    {
        return new LiquidationSum(Volume + size, Turnover + size * price, Count + 1);
    }
}