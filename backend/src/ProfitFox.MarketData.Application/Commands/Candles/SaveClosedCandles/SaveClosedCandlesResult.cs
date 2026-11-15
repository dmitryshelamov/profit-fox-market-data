namespace ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;

/// <summary>
///     How many of the closed candles were saved; the others are of series not started yet or were told again later.
/// </summary>
public sealed record SaveClosedCandlesResult(int Saved);