using ProfitFox.MarketData.Domain.Entities.Instruments;

namespace ProfitFox.MarketData.Domain.Repositories;

/// <summary>
///     Stores the instruments of the catalog, listed or delisted.
/// </summary>
public interface IInstrumentRepository
{
    /// <summary>
    ///     Every instrument the catalog ever kept.
    /// </summary>
    Task<IReadOnlyList<Instrument>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The instrument of the id, listed or delisted; null when the catalog never kept it.
    /// </summary>
    Task<Instrument?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Adds the instruments or replaces the stored ones of the same id, all together.
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken);
}