using MediatR;

using ProfitFox.MarketData.Application.Contracts.Liquidations;

namespace ProfitFox.MarketData.Application.Queries.Liquidations;

/// <summary>
///     The minutes of liquidations storage holds for the instrument in the window, valid or not, as they were summed; it loads
///     nothing. A minute that was heard in full and had no liquidation is not stored and is not told.
/// </summary>
public sealed record GetLiquidationsQuery(Guid InstrumentId, PointWindow Window) : IRequest<LiquidationMinutesInternal>;