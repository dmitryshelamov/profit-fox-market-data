namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>InstrumentSync:TInvest</c> section: whether the catalog syncs the shares of TQBR from T-Invest; it needs <c>TInvestApi:Token</c>.
/// </summary>
internal sealed class TInvestInstrumentSyncSettings
{
    public bool Enabled { get; init; }
}