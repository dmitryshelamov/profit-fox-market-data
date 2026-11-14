using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Application.Ports.Tickers;

/// <summary>
///     The tickers of Bybit as they are now.
/// </summary>
public interface IBybitTickersClient
{
    /// <summary>
    ///     The ticker of every linear contract Bybit lists, the whole market in one request.
    /// </summary>
    /// <exception cref="ExchangePausedException">Bybit limited the requests, now or before the pause ended.</exception>
    /// <exception cref="TransientExchangeException">Bybit is unavailable: the retries of a transient failure ran out.</exception>
    Task<IReadOnlyList<BybitLinearTicker>> GetLinearAsync(CancellationToken cancellationToken);
}