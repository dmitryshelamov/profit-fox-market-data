using MediatR;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.EnsureCandlesLoaded;

/// <summary>
///     Before a read: loads the closed minutes of <see cref="Range" /> storage does not hold, as far as the read may wait, which is
///     <see cref="MaxExchangeRequests" /> requests to the exchange. A range that needs more, or an exchange that takes no requests now,
///     is handed to the background loading, and the range is <see cref="CandlesReadiness.Loading" />.
/// </summary>
public sealed record EnsureCandlesLoadedCommand(Guid InstrumentId, MinuteRange Range, DateTimeOffset RequestedAt, int MaxExchangeRequests)
    : IRequest<CandlesReadiness>;