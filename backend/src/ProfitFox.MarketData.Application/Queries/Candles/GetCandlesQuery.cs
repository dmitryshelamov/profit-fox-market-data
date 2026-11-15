using MediatR;

using ProfitFox.MarketData.Application.Contracts.Candles;

namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     The candles of the window storage holds for the instrument, built from its stored minutes; it loads nothing.
/// </summary>
public sealed record GetCandlesQuery(Guid InstrumentId, CandleWindow Window) : IRequest<CandlesInternal>;