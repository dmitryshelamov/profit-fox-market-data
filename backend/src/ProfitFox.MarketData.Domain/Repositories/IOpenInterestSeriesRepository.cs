using ProfitFox.MarketData.Domain.Entities.OpenInterest;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the open interest series of the instruments: the coverage of each and the points loaded for it.
/// </summary>
public interface IOpenInterestSeriesRepository
{
    /// <summary>
    ///     The series of the instrument, with no new points; null when none of its history is loaded yet.
    /// </summary>
    Task<OpenInterestSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>
    ///     How many points of the instrument storage holds in <c>[from, until)</c>, collected or loaded.
    /// </summary>
    Task<int> CountPointsAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the coverage of the series and its new points together. A point storage already holds stays as it is: the collected
    ///     value is exact, and the history of an exchange may tell the same one rounded.
    /// </summary>
    Task SaveAsync(OpenInterestSeries series, CancellationToken cancellationToken);
}