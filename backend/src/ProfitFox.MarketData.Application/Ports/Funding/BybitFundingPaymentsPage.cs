using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Application.Ports.Funding;

/// <summary>
///     What one request to Bybit gave of the funding payments of a range: the payments, oldest first, and whether the page was
///     <see cref="Full" />. A full page may not be all of the range: Bybit gives the newest payments of it, so older ones may be left.
/// </summary>
public sealed record BybitFundingPaymentsPage(IReadOnlyList<FundingPayment> Payments, bool Full);