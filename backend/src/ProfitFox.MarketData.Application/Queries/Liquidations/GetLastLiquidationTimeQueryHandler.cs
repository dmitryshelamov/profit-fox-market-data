using MediatR;

using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Application.Queries.Liquidations;

internal sealed class GetLastLiquidationTimeQueryHandler(ILiquidationRepository liquidations) : IRequestHandler<GetLastLiquidationTimeQuery, DateTimeOffset?>
{
    public Task<DateTimeOffset?> Handle(GetLastLiquidationTimeQuery request, CancellationToken cancellationToken)
    {
        return liquidations.GetLastTimeAsync(cancellationToken);
    }
}