using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the open interest of the instruments, point by point.
/// </summary>
public interface IOpenInterestRepository
{
    /// <summary>
    ///     Writes the points, all of them together or none. A stored point of the same instrument and time takes the new value.
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<OpenInterestPoint> points, CancellationToken cancellationToken);
}