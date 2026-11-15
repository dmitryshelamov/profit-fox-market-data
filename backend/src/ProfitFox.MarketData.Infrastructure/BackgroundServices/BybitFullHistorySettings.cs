namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>FullHistory:Bybit</c> section of the settings: the symbols of Bybit whose whole history is loaded, in every category the
///     catalog lists them in (<c>BTCUSDT</c> is both a spot pair and a linear contract).
/// </summary>
internal sealed class BybitFullHistorySettings
{
    public string[] Symbols { get; init; } = [];
}