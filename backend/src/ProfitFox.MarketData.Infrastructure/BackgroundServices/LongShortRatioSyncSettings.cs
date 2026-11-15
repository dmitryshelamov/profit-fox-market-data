namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>LongShortRatioSync</c> section of the settings: whether the long/short ratio of Bybit is loaded, how far back, how many
///     contracts at a time, and how long a caught-up loading waits before it looks again.
/// </summary>
internal sealed class LongShortRatioSyncSettings
{
    public const string Section = "LongShortRatioSync";

    /// <summary>
    ///     Days of history back from now that every listed contract loads, not before it was launched.
    /// </summary>
    public int HistoryDays { get; init; } = 365;

    /// <summary>
    ///     Seconds the loading waits before its next round once a round found nothing left to load. Bybit gives the ratio a contract a
    ///     request, so a round of the market is a request a contract, and the newest points are this old at most: an hour by default.
    /// </summary>
    public int IdleSeconds { get; init; } = 3600;

    /// <summary>
    ///     How many contracts are asked for at the same time. The loading shares the limit of Bybit with the loading of candles, so
    ///     more of them here means less of the limit there.
    /// </summary>
    public int Parallelism { get; init; } = 2;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();
}