namespace ProfitFox.MarketData.Domain.ValueObjects.Candles;

/// <summary>
///     What the trades of an instrument seen over one minute add up to: how much was traded and how much of it in trades the buyer
///     started. It tells the buy volume of the candle of that minute, see <see cref="Candle.WithBuyVolume" />.
/// </summary>
public sealed record TradedVolume
{
    /// <summary>
    ///     A minute no trade was seen in.
    /// </summary>
    public static TradedVolume None { get; } = new TradedVolume(traded: 0m, bought: 0m);

    public decimal Traded { get; }

    public decimal Bought { get; }

    public TradedVolume(decimal traded, decimal bought)
    {
        Traded = traded;
        Bought = bought;
    }

    /// <summary>
    ///     The volume with one more trade of <paramref name="quantity" /> counted; <paramref name="bought" /> when the buyer started it.
    /// </summary>
    public TradedVolume Add(decimal quantity, bool bought)
    {
        return new TradedVolume(Traded + quantity, bought ? Bought + quantity : Bought);
    }
}