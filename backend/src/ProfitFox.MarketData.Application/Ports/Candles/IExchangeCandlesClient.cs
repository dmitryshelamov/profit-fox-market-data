using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The minute candles one exchange gives for its listings; every exchange has a client of its own, picked by <see cref="Exchange" />.
/// </summary>
public interface IExchangeCandlesClient
{
    Exchange Exchange { get; }

    /// <summary>
    ///     The longest range one request to the exchange gives.
    /// </summary>
    TimeSpan PageLength { get; }

    /// <summary>
    ///     Every closed candle of the listing that opens inside <paramref name="range" />, oldest first, in one request to the exchange; the
    ///     range is at most <see cref="PageLength" /> long. The candle of a minute that is still open is left out.
    /// </summary>
    /// <exception cref="ExchangePausedException">The exchange limited the requests, now or before the pause ended.</exception>
    /// <exception cref="TransientExchangeException">The exchange is unavailable: the retries of a transient failure ran out.</exception>
    Task<IReadOnlyList<Candle>> GetAsync(ListedInstrument listing, MinuteRange range, CancellationToken cancellationToken);

    /// <summary>
    ///     The first moment the listing may have traded, or null when the exchange does not tell. An exchange that has to look for it in
    ///     its candles looks no further back than <paramref name="since" />; one that tells it outright ignores <paramref name="since" />.
    /// </summary>
    Task<DateTimeOffset?> GetTradingStartAsync(ListedInstrument listing, DateTimeOffset since, CancellationToken cancellationToken);
}