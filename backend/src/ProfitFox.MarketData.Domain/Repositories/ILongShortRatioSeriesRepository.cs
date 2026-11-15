using ProfitFox.MarketData.Domain.Entities.LongShortRatio;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the long/short ratio series of the instruments: the coverage of each and the points loaded for it.
/// </summary>
public interface ILongShortRatioSeriesRepository
{
    /// <summary>
    ///     The series of the instrument, with no new points; null when none of its history is loaded yet.
    /// </summary>
    Task<LongShortRatioSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the coverage of the series and its new points together. A point storage already holds stays as it is.
    /// </summary>
    Task SaveAsync(LongShortRatioSeries series, CancellationToken cancellationToken);
}