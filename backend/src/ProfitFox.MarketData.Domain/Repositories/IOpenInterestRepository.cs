using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the open interest of the instruments, point by point.
/// </summary>
public interface IOpenInterestRepository
{
    /// <summary>
    ///     Writes the points, all of them together or none. A stored point of the same instrument and time takes the new value, one
    ///     loaded from the history too: the collected value is the exact one. No coverage is touched: the loading covers these steps
    ///     later, counting them (<see cref="IOpenInterestSeriesRepository.CountPointsAsync" />).
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<OpenInterestPoint> points, CancellationToken cancellationToken);
}