namespace ProfitFox.MarketData.Application.Contracts.Liquidations;

/// <summary>
///     What the liquidated positions of one side of an instrument add up to over a minute, as it is stored: the volume in the base
///     asset, the turnover in the quote asset (size times the bankruptcy price of each position) and how many liquidations were
///     summed.
/// </summary>
public sealed record LiquidationSumInternal(decimal Volume, decimal Turnover, int Count);