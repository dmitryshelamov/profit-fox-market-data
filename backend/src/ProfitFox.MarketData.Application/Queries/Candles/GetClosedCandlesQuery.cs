using MediatR;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Queries.Candles;

/// <summary>
///     The stored minute candles of the instruments of <see cref="Exchanges" /> that opened at or after <see cref="Since" />, oldest
///     first. They come as a stream and are never all in memory: a day of the whole market is about two million candles.
/// </summary>
public sealed record GetClosedCandlesQuery(DateTimeOffset Since, IReadOnlyCollection<Exchange> Exchanges) : IStreamRequest<ClosedCandleInternal>;