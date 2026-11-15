using MediatR;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;

namespace ProfitFox.MarketData.Application.Queries.OpenInterest;

/// <summary>
///     The stored points of open interest of every instrument of a time at or after <see cref="Since" />, oldest first. They come as a
///     stream and are never all in memory: a day of the whole market is about a quarter of a million points.
/// </summary>
public sealed record GetOpenInterestSinceQuery(DateTimeOffset Since) : IStreamRequest<InstrumentOpenInterestInternal>;