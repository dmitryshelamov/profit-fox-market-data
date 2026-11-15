namespace ProfitFox.MarketData.Application.Ports.Liquidations;

/// <summary>
///     One liquidation of a position in a linear contract of Bybit, as its stream told it: when, of which side, how much of the base
///     asset and the bankruptcy price of the position. <see cref="OfLong" /> when the liquidated position was a long one.
/// </summary>
public sealed record BybitLinearLiquidation(string Symbol, DateTimeOffset Time, bool OfLong, decimal Size, decimal Price);