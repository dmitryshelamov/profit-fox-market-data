using MediatR;

using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

namespace ProfitFox.MarketData.Application.Commands.Instruments.Base;

/// <summary>
///     Syncs the instruments of one market with the list of its exchange; an exchange tells only how to read its list
///     (<see cref="ListAsync" />), which exchange it is (<see cref="Exchange" />), what identifies a listing (<see cref="Key" />) and which
///     stored listings are of the market (<see cref="InMarket" />). The instruments of other markets stay as they are.
/// </summary>
internal abstract class BaseSyncInstrumentsCommandHandler<TCommand, TListing>(IInstrumentRepository instruments) : IRequestHandler<TCommand>
    where TCommand : BaseSyncInstrumentsCommand
    where TListing : ListedInstrument
{
    /// <summary>
    ///     The exchange of the listings, whose stored instruments the sync reads.
    /// </summary>
    protected abstract Exchange Exchange { get; }

    /// <summary>
    ///     Every stored instrument of the market takes its new listing, or is delisted at the request if the list has it no more, and a
    ///     listing the catalog does not know becomes an instrument first seen then; all of them are saved together.
    /// </summary>
    public async Task Handle(TCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<TListing> listed = await ListAsync(request, cancellationToken);
        IReadOnlyList<Instrument> ofExchange = await instruments.GetByExchangeAsync(Exchange, cancellationToken);
        DateTimeOffset now = request.RequestedAt;

        var stored = ofExchange.Where(instrument => InMarket(request, (TListing)instrument.Listing)).ToList();
        var listingsByKey = listed.ToDictionary(Key);

        foreach (Instrument instrument in stored)
        {
            if (listingsByKey.TryGetValue(Key((TListing)instrument.Listing), out TListing? listing))
            {
                instrument.Refresh(listing);
            }
            else
            {
                instrument.Delist(now);
            }
        }

        var storedKeys = stored.Select(instrument => Key((TListing)instrument.Listing)).ToHashSet();

        IEnumerable<Instrument> added = listed
            .Where(listing => !storedKeys.Contains(Key(listing)))
            .Select(listing => Instrument.Create(Guid.CreateVersion7(now), listing, now));

        await instruments.SaveAsync([.. stored, .. added], cancellationToken);
    }

    /// <summary>
    ///     Every listing of the market the exchange has now, the whole list or none: a list cut short would delist the rest.
    /// </summary>
    protected abstract Task<IReadOnlyList<TListing>> ListAsync(TCommand request, CancellationToken cancellationToken);

    /// <summary>
    ///     What identifies a listing within its market, so a stored instrument and its new listing are matched.
    /// </summary>
    protected abstract string Key(TListing listing);

    /// <summary>
    ///     Whether a stored listing is of the market of the request; every listing of the exchange is, unless the exchange has several markets.
    /// </summary>
    protected virtual bool InMarket(TCommand request, TListing listing)
    {
        return true;
    }
}