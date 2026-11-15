using MediatR;

using ProfitFox.MarketData.Application.Contracts.Funding;

namespace ProfitFox.MarketData.Application.Queries.Funding;

/// <summary>
///     The stored funding payments of every instrument made at or after <see cref="Since" />, oldest first, as a stream.
/// </summary>
public sealed record GetFundingPaymentsSinceQuery(DateTimeOffset Since) : IStreamRequest<InstrumentFundingPaymentInternal>;