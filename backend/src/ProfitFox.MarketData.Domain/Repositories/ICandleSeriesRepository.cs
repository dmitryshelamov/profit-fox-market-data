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
    ///     The series of those of the instruments that have one, each with no new candles.
    /// </summary>
    Task<IReadOnlyCollection<CandleSeries>> GetAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the covered ranges of the series and its new candles together. A stored candle of the same minute takes the new values,
    ///     but keeps its buy volume when the new candle knows none.
    /// </summary>
    Task SaveAsync(CandleSeries series, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes several series as one is written, all of them together or none.
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<CandleSeries> series, CancellationToken cancellationToken);
}