using ProfitFox.MarketData.Domain.Entities.Funding;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the funding payment series of the instruments: the coverage of each and its payments.
/// </summary>
public interface IFundingPaymentSeriesRepository
{
    /// <summary>
    ///     The series of the instrument, with no new payments; null when none of its payments is loaded yet.
    /// </summary>
    Task<FundingPaymentSeries?> GetAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the coverage of the series and its new payments together. A stored payment of the same time takes the new rate.
    /// </summary>
    Task SaveAsync(FundingPaymentSeries series, CancellationToken cancellationToken);
}