namespace ProfitFox.MarketData.Application.Contracts.Funding;

/// <summary>
///     The stored funding payments of a window, oldest first, and how far storage holds the payments of the instrument: every payment
///     from <see cref="CoveredFrom" /> up to <see cref="CoveredUntil" /> is stored, so a time without a payment there had none, and a
///     time outside is not loaded; both are null when nothing is loaded. When the window holds more than an answer gives,
///     <see cref="ContinueFrom" /> is the time of the first payment left out, to ask the rest from; null when the answer gives all of them.
/// </summary>
public sealed record FundingPaymentsInternal(
    IReadOnlyList<FundingPaymentInternal> Points,
    DateTimeOffset? CoveredFrom,
    DateTimeOffset? CoveredUntil,
    DateTimeOffset? ContinueFrom);