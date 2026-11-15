using MediatR;

using ProfitFox.MarketData.Application.Contracts.Funding;

namespace ProfitFox.MarketData.Application.Queries.Funding;

/// <summary>
///     The funding payments storage holds for the instrument in the window, with how far it holds them; it loads nothing.
/// </summary>
public sealed record GetFundingPaymentsQuery(Guid InstrumentId, PointWindow Window) : IRequest<FundingPaymentsInternal>;