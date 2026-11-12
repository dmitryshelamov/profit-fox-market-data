using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Base;

namespace ProfitFox.MarketData.Application.Queries.Instruments;

/// <summary>
///     Every share of T-Invest in the catalog, listed or delisted.
/// </summary>
public sealed record GetTInvestSharesQuery : IQuery<IReadOnlyCollection<InstrumentInternal>>;