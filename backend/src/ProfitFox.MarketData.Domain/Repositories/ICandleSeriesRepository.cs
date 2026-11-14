using ProfitFox.MarketData.Domain.Entities.Candles;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the candle series of the instruments: the covered ranges of each and its candles.
/// </summary>
public interface ICandleSeriesRepository
{
    /// <summary>
    ///     The series of the instrument, with no new candles; null when none of its minutes is loaded yet.
    /// </summary>
    Task<CandleSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the covered ranges of the series and its new candles together, replacing a stored candle of the same minute.
    /// </summary>
    Task SaveAsync(CandleSeries series, CancellationToken cancellationToken);
}