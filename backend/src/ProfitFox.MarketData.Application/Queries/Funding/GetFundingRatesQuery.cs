using MediatR;

using ProfitFox.MarketData.Application.Contracts.Funding;

namespace ProfitFox.MarketData.Application.Queries.Funding;

/// <summary>
///     The points of the funding rate of the coming payment storage holds for the instrument in the window, as they were collected; it
///     loads nothing.
/// </summary>
public sealed record GetFundingRatesQuery(Guid InstrumentId, PointWindow Window) : IRequest<FundingRatePointsInternal>;