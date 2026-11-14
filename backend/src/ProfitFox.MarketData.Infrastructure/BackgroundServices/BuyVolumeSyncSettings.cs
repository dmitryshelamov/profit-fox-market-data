namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>BuyVolumeSync</c> section of the settings: how far back the buy volumes of the candles of Bybit are filled from its archive
///     of trades, how many archives are read at a time, and how long the service waits once nothing is left to fill.
/// </summary>
internal sealed class BuyVolumeSyncSettings
{
    public const string Section = "BuyVolumeSync";

    /// <summary>
    ///     Days of history back from now whose candles get their buy volumes.
    /// </summary>
    public int HistoryDays { get; init; } = 365;

    /// <summary>
    ///     Seconds the service waits before its next pass once a pass found nothing left to fill.
    /// </summary>
    public int IdleSeconds { get; init; } = 600;

    /// <summary>
    ///     How many instruments are worked on at the same time; each reads at most one archive at a time.
    /// </summary>
    public int Parallelism { get; init; } = 4;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();
}