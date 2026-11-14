using ProfitFox.MarketData.Application.Commands.Instruments.Base;
using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;

/// <summary>
///     Syncs the instruments of a category of Bybit, matched by symbol: a symbol is unique only within its category.
/// </summary>
internal sealed class SyncBybitInstrumentsCommandHandler(IBybitInstrumentsClient bybit, IInstrumentRepository instruments)
    : BaseSyncInstrumentsCommandHandler<SyncBybitInstrumentsCommand, BybitListedInstrument>(instruments)
{
    protected override Exchange Exchange => Exchange.Bybit;

    protected override Task<IReadOnlyList<BybitListedInstrument>> ListAsync(SyncBybitInstrumentsCommand request, CancellationToken cancellationToken)
    {
        return bybit.ListAsync(request.Category, cancellationToken);
    }

    protected override string Key(BybitListedInstrument listing)
    {
        return listing.Symbol;
    }

    protected override bool InMarket(SyncBybitInstrumentsCommand request, BybitListedInstrument listing)
    {
        return listing.Category == request.Category;
    }
}