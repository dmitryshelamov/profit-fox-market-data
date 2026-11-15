using MediatR;

using ProfitFox.MarketData.Application.Contracts.Liquidations;

namespace ProfitFox.MarketData.Application.Queries.Liquidations;

/// <summary>
///     The stored minutes of liquidations of every instrument of a time at or after <see cref="Since" />, valid or not, oldest first.
///     They come as a stream and are never all in memory: a day the service was down is more than a million minutes that are not
///     valid.
/// </summary>
public sealed record GetLiquidationsSinceQuery(DateTimeOffset Since) : IStreamRequest<InstrumentLiquidationMinuteInternal>;