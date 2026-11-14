namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The section of an exchange in <c>CandleSync</c>: whether its instruments load candles; T-Invest needs <c>TInvestApi:Token</c>.
/// </summary>
internal sealed class CandleSyncExchangeSettings
{
    public bool Enabled { get; init; }
}