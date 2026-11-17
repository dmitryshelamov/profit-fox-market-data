using MediatR;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Queries.Funding;

/// <summary>
///     The coverage of the history of funding payments over the window: of every instrument named, or of every listed one of
///     <see cref="Exchanges" /> when none is named (of all exchanges when none is either), ordered by id. A delisted instrument is left
///     out, even when named. An instrument named that is not in the catalog fails with
///     <see cref="Exceptions.InstrumentNotFoundException" />.
/// </summary>
public sealed record GetFundingPaymentHistoryCoverageQuery(IReadOnlyCollection<Guid> InstrumentIds, IReadOnlyCollection<Exchange> Exchanges, HistoryWindow Window)
    : IRequest<IReadOnlyList<InstrumentCoverageInternal>>;