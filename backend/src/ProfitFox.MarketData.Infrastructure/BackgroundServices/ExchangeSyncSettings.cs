namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The section of an exchange in the settings of a background service: whether the service works on that exchange. T-Invest needs
///     <c>TInvestApi:Token</c> while it is enabled.
/// </summary>
internal sealed class ExchangeSyncSettings
{
    public bool Enabled { get; init; }
}