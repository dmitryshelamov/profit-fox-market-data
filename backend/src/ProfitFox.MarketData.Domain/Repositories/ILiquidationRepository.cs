using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the liquidations of the instruments, summed minute by minute. A stored minute is valid while the liquidations of it
///     were all heard; one that was not heard in full is marked not valid and its sums tell only what was heard, down to nothing.
///     A minute that is not stored was heard in full and had no liquidation.
/// </summary>
public interface ILiquidationRepository
{
    /// <summary>
    ///     Adds the sums to the stored ones of the same instrument and minute, all of them together or none; a minute not stored yet
    ///     is stored as a valid one, a stored one keeps its mark. A minute of an instrument comes once in
    ///     <paramref name="minutes" />.
    /// </summary>
    Task AddAsync(IReadOnlyCollection<LiquidationMinute> minutes, CancellationToken cancellationToken);

    /// <summary>
    ///     Marks every minute of the instruments from <paramref name="from" /> to <paramref name="until" />, both included, as not
    ///     valid, all of them together or none. A stored minute keeps its sums; one not stored is stored with sums of nothing. The mark
    ///     is never taken back.
    /// </summary>
    Task InvalidateAsync(IReadOnlyCollection<Guid> instrumentIds, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken);

    /// <summary>
    ///     The latest minute stored of any instrument, valid or not; null while nothing is stored.
    /// </summary>
    Task<DateTimeOffset?> GetLastTimeAsync(CancellationToken cancellationToken);
}