using MediatR;

namespace ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;

/// <summary>
///     Takes a snapshot of the linear market of Bybit, one request for all of it, and saves it as the points of <see cref="Time" />, the
///     minute boundary the snapshot is taken for: the open interest of every listed linear contract of the catalog and the funding rate
///     of its coming payment, each for the contracts whose ticker tells it. The two series are saved together or not at all.
/// </summary>
public sealed record CollectBybitTickersCommand(DateTimeOffset Time) : IRequest<CollectBybitTickersResult>;