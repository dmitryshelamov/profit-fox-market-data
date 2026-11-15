using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The public archive of the trades of Bybit, one file a day of an instrument, published some hours after the day ends. It tells the
///     buy volume of the candles of days gone by, which neither the candles Bybit gives on request nor a stream that was not listening
///     can tell.
/// </summary>
public interface IBybitTradeArchiveClient
{
    /// <summary>
    ///     The trades of the UTC <paramref name="day" /> of the listing, summed by the minute they were made in and found by the start of
    ///     that minute; a minute without trades is not there. Null when Bybit has no archive of the day: it is not published yet, or the
    ///     instrument has none.
    /// </summary>
    /// <exception cref="TransientExchangeException">The archive cannot be read for now: the retries of a transient failure ran out.</exception>
    Task<IReadOnlyDictionary<DateTimeOffset, TradedVolume>?> GetTradesAsync(
        BybitListedInstrument listing,
        DateOnly day,
        CancellationToken cancellationToken);
}