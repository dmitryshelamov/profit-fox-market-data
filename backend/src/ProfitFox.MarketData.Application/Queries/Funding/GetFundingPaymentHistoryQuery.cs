using MediatR;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.Funding;

namespace ProfitFox.MarketData.Application.Queries.Funding;

/// <summary>
///     The stored funding payments inside <see cref="Coverage" />, by time and then by instrument id, in an order that is the same on every
///     read. Given the coverage read before it, it reads nothing loaded since. They come as a stream, a day of the window at a time, and
///     are never all in memory.
/// </summary>
public sealed record GetFundingPaymentHistoryQuery(IReadOnlyList<InstrumentCoverageInternal> Coverage) : IStreamRequest<InstrumentFundingPaymentInternal>;