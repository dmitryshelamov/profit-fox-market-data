namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>LiquidationStream</c> section of the settings: whether the liquidations of Bybit are listened to, how often the catalog
///     is read again for contracts to subscribe to, and how often what was heard is saved.
/// </summary>
internal sealed class LiquidationStreamSettings
{
    public const string Section = "LiquidationStream";

    /// <summary>
    ///     Seconds between two reads of the catalog for contracts listed since the last one.
    /// </summary>
    public int RefreshSeconds { get; init; } = 60;

    /// <summary>
    ///     Seconds between two saves of the sums heard since the save before. A stop of the app loses what was heard after the last
    ///     save, and the minutes it restarts in are marked not valid for it.
    /// </summary>
    public int SaveSeconds { get; init; } = 5;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();
}