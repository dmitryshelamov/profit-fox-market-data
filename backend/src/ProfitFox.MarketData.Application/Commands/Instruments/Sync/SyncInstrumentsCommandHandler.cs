using MediatR;

using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Commands.Instruments.Sync;

internal sealed class SyncInstrumentsCommandHandler(
    IExchangeInstruments exchange,
    IInstrumentRepository instruments) : IRequestHandler<SyncInstrumentsCommand>
{
    /// <summary>
    ///     Every stored instrument of the category takes its new listing, or is delisted if the list has it no more; an instrument of the list
    ///     the catalog does not know is added. The instruments of other categories stay as they are.
    /// </summary>
    public async Task Handle(SyncInstrumentsCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<BybitListedInstrument> listed = await exchange.ListAsync(request.Category, cancellationToken);
        IReadOnlyList<Instrument> catalog = await instruments.GetAllAsync(cancellationToken);
        var stored = catalog.Where(instrument => instrument.Listing.Category == request.Category).ToList();
        DateTimeOffset now = request.RequestedAt;

        var listingsBySymbol = listed.ToDictionary(listing => listing.Symbol);

        foreach (Instrument instrument in stored)
        {
            if (listingsBySymbol.TryGetValue(instrument.Listing.Symbol, out BybitListedInstrument? listing))
            {
                instrument.Refresh(listing);
            }
            else
            {
                instrument.Delist(now);
            }
        }

        var storedSymbols = stored.Select(instrument => instrument.Listing.Symbol).ToHashSet();

        IEnumerable<Instrument> added = listed
            .Where(listing => !storedSymbols.Contains(listing.Symbol))
            .Select(listing => Instrument.Create(Guid.CreateVersion7(now), listing, now));

        await instruments.SaveAsync([.. stored, .. added], cancellationToken);
    }
}