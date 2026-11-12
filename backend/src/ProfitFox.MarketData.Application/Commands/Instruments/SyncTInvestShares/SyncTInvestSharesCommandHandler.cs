using ProfitFox.MarketData.Application.Commands.Instruments.Base;
using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Commands.Instruments.SyncTInvestShares;

/// <summary>
///     Syncs the shares of T-Invest, matched by uid, so a share that changed its ticker stays the same instrument.
/// </summary>
internal sealed class SyncTInvestSharesCommandHandler(ITInvestShares shares, IInstrumentRepository instruments)
    : BaseSyncInstrumentsCommandHandler<SyncTInvestSharesCommand, TInvestListedShare>(instruments)
{
    protected override Task<IReadOnlyList<TInvestListedShare>> ListAsync(SyncTInvestSharesCommand request, CancellationToken cancellationToken)
    {
        return shares.ListAsync(cancellationToken);
    }

    protected override string Key(TInvestListedShare share)
    {
        return share.Uid;
    }
}