namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>TickerCollection</c> section of the settings: whether the open interest and the funding rates of Bybit are collected every
///     minute, and how long a snapshot may take.
/// </summary>
internal sealed class TickerCollectionSettings
{
    public const string Section = "TickerCollection";

    /// <summary>
    ///     Seconds after a minute boundary by which its snapshot must be taken and saved; a later one would no longer tell that minute, so
    ///     the minute is skipped. Below 60: a snapshot is given up before the next minute starts.
    /// </summary>
    public int DeadlineSeconds { get; init; } = 10;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();
}