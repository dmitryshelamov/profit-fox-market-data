using MediatR;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.Candles;

namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     The candles of <see cref="Interval" /> whose minutes are all inside <see cref="Coverage" />, by open time and then by instrument id,
///     in an order that is the same on every read. Given the coverage read before it, it reads nothing loaded since. They come as a stream,
///     a day of the window at a time, and are never all in memory.
/// </summary>
public sealed record GetCandleHistoryQuery(IReadOnlyList<InstrumentCoverageInternal> Coverage, CandleInterval Interval) : IStreamRequest<ClosedCandleInternal>;