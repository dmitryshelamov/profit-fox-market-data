using MediatR;

using ProfitFox.MarketData.Application.Contracts.Funding;

namespace ProfitFox.MarketData.Application.Queries.Funding;

/// <summary>
///     The stored points of the funding rate of the coming payment of every instrument of a time at or after <see cref="Since" />,
///     oldest first. They come as a stream and are never all in memory: a day of the whole market is about a quarter of a million
///     points.
/// </summary>
public sealed record GetFundingRatesSinceQuery(DateTimeOffset Since) : IStreamRequest<InstrumentFundingRateInternal>;