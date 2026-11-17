using MediatR;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.LongShortRatio;

namespace ProfitFox.MarketData.Application.Queries.LongShortRatio;

/// <summary>
///     The stored points of the long/short ratio inside <see cref="Coverage" />, by time and then by instrument id, in an order that is the
///     same on every read. Given the coverage read before it, it reads nothing loaded since. They come as a stream, a day of the window at
///     a time, and are never all in memory.
/// </summary>
public sealed record GetLongShortRatioHistoryQuery(IReadOnlyList<InstrumentCoverageInternal> Coverage) : IStreamRequest<InstrumentLongShortRatioInternal>;