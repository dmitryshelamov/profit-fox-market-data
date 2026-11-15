namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>TickerCollection</c> section of the settings: whether the open interest and the funding rates of Bybit are collected,
///     every five minutes, and how long a snapshot may take.
/// </summary>
internal sealed class TickerCollectionSettings
{
    public const string Section = "TickerCollection";

    /// <summary>
    ///     Seconds after the boundary of a step by which its snapshot must be taken and saved; a later one would no longer tell that
    ///     moment, so the step is skipped. Below 60, whatever the step: a value taken a minute after a boundary is not the value of
    ///     that boundary, and the history of the exchange would tell another one.
    /// </summary>
    public int DeadlineSeconds { get; init; } = 10;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();
}