using MediatR;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;

namespace ProfitFox.MarketData.Application.Queries.LongShortRatio;

/// <summary>
///     The stored points of the long/short ratio of every instrument of a time at or after <see cref="Since" />, oldest first. They
///     come as a stream and are never all in memory: a day of the whole market is about a quarter of a million points.
/// </summary>
public sealed record GetLongShortRatioSinceQuery(DateTimeOffset Since) : IStreamRequest<InstrumentLongShortRatioInternal>;