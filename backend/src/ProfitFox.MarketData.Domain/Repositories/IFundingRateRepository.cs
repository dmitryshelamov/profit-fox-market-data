using ProfitFox.MarketData.Domain.ValueObjects.Funding;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the funding rates of the coming payments of the instruments, point by point.
/// </summary>
public interface IFundingRateRepository
{
    /// <summary>
    ///     Writes the points, all of them together or none. A stored point of the same instrument and time takes the new rate.
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<FundingRatePoint> points, CancellationToken cancellationToken);
}