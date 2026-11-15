namespace ProfitFox.MarketData.Application.Contracts.LongShortRatio;

/// <summary>
///     The stored points of the long/short ratio of a window, oldest first, and how far storage holds the history of the instrument:
///     every point its exchange has from <see cref="CoveredFrom" /> up to <see cref="CoveredUntil" /> is stored, and a time outside is
///     not loaded yet; both are null when nothing is loaded. When the window holds more than an answer gives,
///     <see cref="ContinueFrom" /> is the time of the first point left out, to ask the rest from; null when the answer gives all of
///     them.
/// </summary>
public sealed record LongShortRatioPointsInternal(
    IReadOnlyList<LongShortRatioPointInternal> Points,
    DateTimeOffset? CoveredFrom,
    DateTimeOffset? CoveredUntil,
    DateTimeOffset? ContinueFrom);