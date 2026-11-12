using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Base;

namespace ProfitFox.MarketData.Application.Queries.Instruments;

/// <summary>
///     Every instrument of the catalog, listed or delisted.
/// </summary>
public sealed record GetInstrumentsQuery : IQuery<IReadOnlyCollection<InstrumentInternal>>;