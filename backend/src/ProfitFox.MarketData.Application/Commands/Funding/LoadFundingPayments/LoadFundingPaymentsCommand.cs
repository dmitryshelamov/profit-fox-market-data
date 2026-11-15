using MediatR;

namespace ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;

/// <summary>
///     Loads one page of the funding payments of the instrument its series does not cover between <see cref="From" /> and
///     <see cref="RequestedAt" />: one request to its exchange, saved at once. The payments made since the coverage ended come first,
///     then the history, the newest page of it first. A caller sends it again, with the same <see cref="RequestedAt" />, while it gives
///     <see cref="LoadFundingPaymentsResult.MoreLeft" />.
/// </summary>
public sealed record LoadFundingPaymentsCommand(Guid InstrumentId, DateTimeOffset From, DateTimeOffset RequestedAt) : IRequest<LoadFundingPaymentsResult>;