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
    ///     The <paramref name="candles" /> of the UTC <paramref name="day" /> of the listing, each with the buy volume the trades of its
    ///     minute in the archive tell. A candle whose trades there do not add up to its volume is given back as it is. Null when Bybit
    ///     has no archive of the day: it is not published yet, or the instrument has none.
    /// </summary>
    /// <exception cref="TransientExchangeException">The archive cannot be read for now: the retries of a transient failure ran out.</exception>
    Task<IReadOnlyList<Candle>?> FillBuyVolumesAsync(
        BybitListedInstrument listing,
        DateOnly day,
        IReadOnlyCollection<Candle> candles,
        CancellationToken cancellationToken);
}