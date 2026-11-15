namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>OpenInterestSync</c> section of the settings: whether the history of open interest of Bybit is loaded, how far back, how
///     many contracts at a time, and how long a caught-up loading waits before it looks again.
/// </summary>
internal sealed class OpenInterestSyncSettings
{
    public const string Section = "OpenInterestSync";

    /// <summary>
    ///     Days of history back from now that every listed linear contract loads, not before it was launched.
    /// </summary>
    public int HistoryDays { get; init; } = 365;

    /// <summary>
    ///     Seconds the loading waits before its next round once a round found nothing left to load. A step the collection missed is
    ///     looked for after this wait.
    /// </summary>
    public int IdleSeconds { get; init; } = 600;

    /// <summary>
    ///     How many contracts are asked for at the same time. The history shares the limit of Bybit with the loading of candles, so
    ///     more of them here means less of the limit there.
    /// </summary>
    public int Parallelism { get; init; } = 2;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();
}