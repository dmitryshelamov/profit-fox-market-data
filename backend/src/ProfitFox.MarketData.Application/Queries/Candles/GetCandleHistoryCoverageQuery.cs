using MediatR;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     The coverage of the history of candles over the window: of every instrument named, or of every listed one of
///     <see cref="Exchanges" /> when none is named (of all exchanges when none is either), ordered by id. A delisted instrument is left
///     out, even when named. Covered on Bybit is what is loaded and whose buy volume the archive settled, on T-Invest what is loaded: what
///     will not change. An instrument named that is not in the catalog fails with <see cref="Exceptions.InstrumentNotFoundException" />.
/// </summary>
public sealed record GetCandleHistoryCoverageQuery(IReadOnlyCollection<Guid> InstrumentIds, IReadOnlyCollection<Exchange> Exchanges, CandleHistoryWindow Window)
    : IRequest<IReadOnlyList<InstrumentCoverageInternal>>;