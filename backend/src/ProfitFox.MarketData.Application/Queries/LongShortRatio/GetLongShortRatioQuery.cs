using MediatR;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;

namespace ProfitFox.MarketData.Application.Queries.LongShortRatio;

/// <summary>
///     The points of the long/short ratio storage holds for the instrument in the window, with how far it holds its history; it loads
///     nothing.
/// </summary>
public sealed record GetLongShortRatioQuery(Guid InstrumentId, PointWindow Window) : IRequest<LongShortRatioPointsInternal>;