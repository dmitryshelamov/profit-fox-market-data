using MediatR;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Instruments;

internal sealed class GetBybitInstrumentsQueryHandler(IInstrumentRepository instruments)
    : IRequestHandler<GetBybitInstrumentsQuery, IReadOnlyCollection<InstrumentInternal>>
{
    public async Task<IReadOnlyCollection<InstrumentInternal>> Handle(GetBybitInstrumentsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Instrument> stored = await instruments.GetAllAsync(cancellationToken);

        return [.. stored.Where(instrument => instrument.Listing is BybitListedInstrument).Select(instrument => instrument.ToInternal())];
    }
}