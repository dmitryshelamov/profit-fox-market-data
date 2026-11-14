using MediatR;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;

namespace ProfitFox.MarketData.Application.Queries.OpenInterest;

/// <summary>
///     The points of open interest storage holds for the instrument in the window, as they were collected; it loads nothing.
/// </summary>
public sealed record GetOpenInterestQuery(Guid InstrumentId, OpenInterestWindow Window) : IRequest<OpenInterestPointsInternal>;