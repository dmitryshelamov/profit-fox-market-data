using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the candle series of the instruments: the covered and the settled ranges of each and its candles.
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
    ///     The stored candles of the instrument that open inside <paramref name="range" /> and have no buy volume, oldest first.
    /// </summary>
    Task<IReadOnlyList<Candle>> GetCandlesWithoutBuyVolumeAsync(Guid instrumentId, MinuteRange range, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the covered and the settled ranges of the series and its new candles together. A stored candle of the same minute takes
    ///     the new values, but keeps its buy volume when the new candle knows none.
    /// </summary>
    Task SaveAsync(CandleSeries series, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes several series as one is written, all of them together or none. Gives the new candles of each as storage holds them
    ///     now, by instrument: one saved without a buy volume has the buy volume its minute already had.
    /// </summary>
    Task<ILookup<Guid, Candle>> SaveAsync(IReadOnlyCollection<CandleSeries> series, CancellationToken cancellationToken);
}