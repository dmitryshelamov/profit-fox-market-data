using MediatR;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;

namespace ProfitFox.MarketData.Application.Queries.OpenInterest;

/// <summary>
///     The points of open interest storage holds for the instrument in the window, with how far it holds its history; it loads
///     nothing.
/// </summary>
public sealed record GetOpenInterestQuery(Guid InstrumentId, PointWindow Window) : IRequest<OpenInterestPointsInternal>;