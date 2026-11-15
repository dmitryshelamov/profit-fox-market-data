using MediatR;

namespace ProfitFox.MarketData.Application.Queries.Liquidations;

/// <summary>
///     The latest minute storage holds liquidations of, of any instrument, valid or not; null while it holds none. The liquidations
///     were heard no later than that minute: a listener that starts tells by it how long nobody listened.
/// </summary>
public sealed record GetLastLiquidationTimeQuery : IRequest<DateTimeOffset?>;