using MediatR;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Contracts.Liquidations.Converters;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;

/// <summary>
///     The feed hears of a minute only after it is saved, and with the sums storage holds, not the ones added: a follower replaces
///     the minute it has, by instrument and time.
/// </summary>
internal sealed class SaveLiquidationsCommandHandler(ILiquidationRepository liquidations, ILiveFeed<InstrumentLiquidationMinuteInternal> feed)
    : IRequestHandler<SaveLiquidationsCommand>
{
    public async Task Handle(SaveLiquidationsCommand request, CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredLiquidationMinute> stored = await liquidations.AddAsync(request.Minutes, cancellationToken);

        feed.Publish([.. stored.Select(minute => minute.ToInternal())]);
    }
}