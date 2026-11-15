using MediatR;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Queries.Instruments;

/// <summary>
///     The instruments of the catalog, listed or delisted: of one exchange, or of every exchange when none is named.
/// </summary>
public sealed record GetInstrumentsQuery(Exchange? Exchange = null) : IRequest<IReadOnlyCollection<InstrumentInternal>>;