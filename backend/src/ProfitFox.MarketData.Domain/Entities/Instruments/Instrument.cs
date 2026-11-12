using ProfitFox.MarketData.Domain.Entities.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Domain.Entities.Instruments;

/// <summary>
///     An instrument the catalog keeps: what its exchange told about it (<see cref="Listing" />) and since when the catalog knows it.
///     Candles refer to it by <see cref="Id" />.
/// </summary>
public sealed class Instrument : AggregateRoot
{
    /// <summary>
    ///     The id of the instrument in the catalog; it never changes.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     What the exchange told about the instrument.
    /// </summary>
    public BybitListedInstrument Listing { get; }

    /// <summary>
    ///     When the catalog first saw the instrument in the list of its exchange.
    /// </summary>
    public DateTimeOffset FirstSeenAt { get; }

    private Instrument(Guid id, BybitListedInstrument listing, DateTimeOffset firstSeenAt)
    {
        Id = id;
        Listing = listing;
        FirstSeenAt = firstSeenAt;
    }

    /// <summary>
    ///     An instrument the catalog sees in the list of its exchange for the first time, at <paramref name="now" />.
    /// </summary>
    public static Instrument Create(Guid id, BybitListedInstrument listing, DateTimeOffset now)
    {
        return new Instrument(id, listing, now);
    }
}