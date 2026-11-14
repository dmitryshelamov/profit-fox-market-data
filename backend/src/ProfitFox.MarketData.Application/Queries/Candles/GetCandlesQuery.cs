using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Base;

namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     The candles of <see cref="Interval" /> of an instrument in <c>[From, To)</c>, which start and end candles of the interval. Minutes
///     storage does not hold are loaded from the exchange first, as far as a read may wait for them; the rest load in the background and
///     the answer says it is loading. Null for an instrument the catalog does not have.
/// </summary>
public sealed record GetCandlesQuery(Guid InstrumentId, CandleInterval Interval, DateTimeOffset From, DateTimeOffset To, DateTimeOffset RequestedAt)
    : IQuery<CandlesInternal?>;