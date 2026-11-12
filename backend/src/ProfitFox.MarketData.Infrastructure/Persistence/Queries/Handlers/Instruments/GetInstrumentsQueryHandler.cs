using MediatR;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Instruments;

internal sealed class GetInstrumentsQueryHandler(IInstrumentRepository instruments)
    : IRequestHandler<GetInstrumentsQuery, IReadOnlyCollection<InstrumentInternal>>
{
    public async Task<IReadOnlyCollection<InstrumentInternal>> Handle(GetInstrumentsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Instrument> stored = await instruments.GetAllAsync(cancellationToken);
        var result = stored.Select(instrument => instrument.ToInternal()).ToList();

        return result;
    }
}