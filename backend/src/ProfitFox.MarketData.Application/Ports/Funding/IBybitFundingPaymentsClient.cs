using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.Funding;

/// <summary>
///     The funding payments Bybit made on its contracts.
/// </summary>
public interface IBybitFundingPaymentsClient
{
    /// <summary>
    ///     The payments of the contract made in <c>[from, until)</c>, in one request to Bybit: all of them, or the newest of them when
    ///     the range holds more than a page.
    /// </summary>
    /// <exception cref="ExchangePausedException">Bybit limited the requests, now or before the pause ended.</exception>
    /// <exception cref="TransientExchangeException">Bybit is unavailable: the retries of a transient failure ran out.</exception>
    Task<BybitFundingPaymentsPage> GetAsync(BybitListedInstrument listing, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken);
}