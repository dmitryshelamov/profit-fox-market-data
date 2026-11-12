namespace ProfitFox.MarketData.Domain.ValueObjects.Instruments;

/// <summary>
///     What the exchange accepts for an instrument: prices on the tick, quantities on the step, and no order below the minimums.
/// </summary>
public sealed record InstrumentRules
{
    /// <summary>
    ///     Step of the price.
    /// </summary>
    public decimal TickSize { get; }

    /// <summary>
    ///     Step of the quantity of an order: of the base asset on spot, of contracts on a derivative.
    /// </summary>
    public decimal QuantityStep { get; }

    /// <summary>
    ///     Smallest quantity of an order.
    /// </summary>
    public decimal MinQuantity { get; }

    /// <summary>
    ///     Smallest value of an order in the quote asset, if the exchange sets one.
    /// </summary>
    public decimal? MinAmount { get; }

    public InstrumentRules(decimal tickSize, decimal quantityStep, decimal minQuantity, decimal? minAmount = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantityStep);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minQuantity);

        if (minAmount is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minAmount.Value, nameof(minAmount));
        }

        TickSize = tickSize;
        QuantityStep = quantityStep;
        MinQuantity = minQuantity;
        MinAmount = minAmount;
    }
}