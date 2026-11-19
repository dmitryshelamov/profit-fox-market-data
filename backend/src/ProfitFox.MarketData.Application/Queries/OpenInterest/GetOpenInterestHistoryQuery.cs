using MediatR;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.OpenInterest;

namespace ProfitFox.MarketData.Application.Queries.OpenInterest;

/// <summary>
///     The stored points of open interest inside <see cref="Coverage" />, by time and then by instrument id, in an order that is the same
///     on every read. Given the coverage read before it, it reads nothing loaded since. They come as a stream, a part of the window at a
///     time, and are never all in memory.
/// </summary>
public sealed record GetOpenInterestHistoryQuery(IReadOnlyList<InstrumentCoverageInternal> Coverage) : IStreamRequest<InstrumentOpenInterestInternal>;