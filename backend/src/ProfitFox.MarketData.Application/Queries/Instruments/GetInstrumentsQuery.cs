using MediatR;

using ProfitFox.MarketData.Application.Contracts.Instruments;

namespace ProfitFox.MarketData.Application.Queries.Instruments;

/// <summary>
///     Every instrument of the catalog, listed or delisted.
/// </summary>
public sealed record GetInstrumentsQuery : IRequest<IReadOnlyCollection<InstrumentInternal>>;