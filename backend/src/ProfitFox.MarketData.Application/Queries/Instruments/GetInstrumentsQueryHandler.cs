using MediatR;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Contracts.Instruments.Converters;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Application.Queries.Instruments;

internal sealed class GetInstrumentsQueryHandler(IInstrumentRepository instruments)
    : IRequestHandler<GetInstrumentsQuery, IReadOnlyCollection<InstrumentInternal>>
{
    public async Task<IReadOnlyCollection<InstrumentInternal>> Handle(GetInstrumentsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Instrument> stored = request.Exchange is { } exchange
            ? await instruments.GetByExchangeAsync(exchange, cancellationToken)
            : await instruments.GetAllAsync(cancellationToken);

        return [.. stored.Select(instrument => instrument.ToInternal())];
    }
}