using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The minute candles of Bybit as they close, each with how much of its volume the buyers took, which the candles Bybit gives on
///     request do not tell.
/// </summary>
public interface IBybitCandleStreamClient
{
    /// <summary>
    ///     Subscribes to the instruments of <paramref name="category" /> with <paramref name="symbols" />. From then on
    ///     <paramref name="closed" /> gets the symbol and the candle of every minute that closes, as Bybit tells it, until the subscription
    ///     is disposed. The buy volume of a candle is null when the trades of its minute were not all seen, as in the minute the
    ///     subscription started in or one the connection broke in; a minute whose close was not seen gets no candle at all. A candle
    ///     may be told again: with its buy volume, once a trade of its minute that came after it completes them, or as Bybit repeats
    ///     it after a reconnect. The connection is restored by the client itself.
    /// </summary>
    /// <exception cref="TransientExchangeException">Bybit cannot be reached or refused the subscription; nothing stays subscribed.</exception>
    Task<IAsyncDisposable> SubscribeAsync(
        BybitCategory category,
        IReadOnlyCollection<string> symbols,
        Action<string, Candle> closed,
        CancellationToken cancellationToken);
}