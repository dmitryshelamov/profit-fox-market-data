using MediatR;

namespace ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;

/// <summary>
///     Takes a snapshot of the linear market of Bybit, one request for all of it, and saves it as the points of <see cref="Time" />,
///     the boundary the snapshot is taken for, where a step of the open interest series starts: the open interest of every listed
///     linear contract of the catalog and the funding rate of its coming payment, each for the contracts whose ticker tells it. Each
///     series is saved whole or not at all, apart from the other.
/// </summary>
public sealed record CollectBybitTickersCommand(DateTimeOffset Time) : IRequest<CollectBybitTickersResult>;