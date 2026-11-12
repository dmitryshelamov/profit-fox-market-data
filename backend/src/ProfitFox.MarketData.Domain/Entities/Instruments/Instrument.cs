using ProfitFox.MarketData.Domain.Entities.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Domain.Entities.Instruments;

/// <summary>
///     An instrument the catalog keeps: what its exchange last told about it (<see cref="Listing" />), since when the catalog knows it and,
///     once it is gone from the list of the exchange, when it went. A delisted instrument stays with its candles; if the exchange lists it
///     again, it is the same instrument and comes back. Candles refer to it by <see cref="Id" />.
/// </summary>
public sealed class Instrument : AggregateRoot
{
    /// <summary>
    ///     The id of the instrument in the catalog; it never changes.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     What the exchange last told about the instrument.
    /// </summary>
    public BybitListedInstrument Listing { get; private set; }

    /// <summary>
    ///     When the catalog first saw the instrument in the list of its exchange.
    /// </summary>
    public DateTimeOffset FirstSeenAt { get; }

    /// <summary>
    ///     When the instrument went from the list of its exchange; none while it is listed.
    /// </summary>
    public DateTimeOffset? DelistedAt { get; private set; }

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

    /// <summary>
    ///     Takes what the exchange tells about the instrument now; a delisted instrument is listed again.
    /// </summary>
    public void Refresh(BybitListedInstrument listing)
    {
        Listing = listing;
        DelistedAt = null;
    }

    /// <summary>
    ///     Marks the instrument gone from the list of its exchange at <paramref name="now" />; one already delisted keeps the time it went.
    /// </summary>
    public void Delist(DateTimeOffset now)
    {
        DelistedAt ??= now;
    }
}