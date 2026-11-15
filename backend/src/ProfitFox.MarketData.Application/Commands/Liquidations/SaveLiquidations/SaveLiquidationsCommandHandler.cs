using MediatR;

using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;

internal sealed class SaveLiquidationsCommandHandler(ILiquidationRepository liquidations) : IRequestHandler<SaveLiquidationsCommand>
{
    public Task Handle(SaveLiquidationsCommand request, CancellationToken cancellationToken)
    {
        return liquidations.AddAsync(request.Minutes, cancellationToken);
    }
}