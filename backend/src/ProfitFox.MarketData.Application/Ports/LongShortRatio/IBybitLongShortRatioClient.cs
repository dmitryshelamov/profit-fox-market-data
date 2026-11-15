using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.LongShortRatio;

/// <summary>
///     The history of the long/short ratio Bybit keeps of its linear contracts quoted in USDT, a point every five minutes. Bybit has no
///     other source of it: the newest point of the history is the freshest value there is, and it is asked a contract a request.
/// </summary>
public interface IBybitLongShortRatioClient
{
    /// <summary>
    ///     The longest range one request to Bybit gives whole.
    /// </summary>
    TimeSpan PageLength { get; }

    /// <summary>
    ///     Every point of the contract in <c>[from, until)</c>, oldest first, in one request to Bybit; the range is at most
    ///     <see cref="PageLength" /> long. None for a range before the contract was listed.
    /// </summary>
    /// <exception cref="ExchangePausedException">Bybit limited the requests, now or before the pause ended.</exception>
    /// <exception cref="TransientExchangeException">Bybit is unavailable: the retries of a transient failure ran out.</exception>
    Task<IReadOnlyList<BybitLongShortRatioPoint>> GetAsync(
        BybitListedInstrument listing,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken);
}