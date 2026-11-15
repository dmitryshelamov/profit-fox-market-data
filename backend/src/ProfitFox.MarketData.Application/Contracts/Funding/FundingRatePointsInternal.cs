namespace ProfitFox.MarketData.Application.Contracts.Funding;

/// <summary>
///     The stored points of the funding rate of a window, oldest first. When the window holds more than an answer gives,
///     <see cref="ContinueFrom" /> is the time of the first point left out, to ask the rest from; null when the answer gives all of
///     them.
/// </summary>
public sealed record FundingRatePointsInternal(IReadOnlyList<FundingRatePointInternal> Points, DateTimeOffset? ContinueFrom);