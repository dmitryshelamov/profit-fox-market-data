using ProfitFox.MarketData.Application.Commands.Instruments.Base;

namespace ProfitFox.MarketData.Application.Commands.Instruments.SyncTInvestShares;

/// <summary>
///     Brings the shares of T-Invest in line with its list as of <see cref="BaseSyncInstrumentsCommand.RequestedAt" />.
/// </summary>
public sealed record SyncTInvestSharesCommand(DateTimeOffset RequestedAt) : BaseSyncInstrumentsCommand(RequestedAt);