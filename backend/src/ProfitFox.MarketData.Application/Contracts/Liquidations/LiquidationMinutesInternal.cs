namespace ProfitFox.MarketData.Application.Contracts.Liquidations;

/// <summary>
///     The stored minutes of liquidations of a window, oldest first. When the window holds more than an answer gives,
///     <see cref="ContinueFrom" /> is the time of the first minute left out, to ask the rest from; null when the answer gives all of
///     them.
/// </summary>
public sealed record LiquidationMinutesInternal(IReadOnlyList<LiquidationMinuteInternal> Minutes, DateTimeOffset? ContinueFrom);