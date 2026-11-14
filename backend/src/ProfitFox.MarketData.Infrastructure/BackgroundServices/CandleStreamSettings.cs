namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>CandleStream</c> section of the settings: which exchanges tell their minute candles as they close, how often the catalog
///     is read again for instruments to subscribe to, and how long closed candles are gathered before they are saved together.
/// </summary>
internal sealed class CandleStreamSettings
{
    public const string Section = "CandleStream";

    /// <summary>
    ///     Seconds between two reads of the catalog for instruments listed since the last one.
    /// </summary>
    public int RefreshSeconds { get; init; } = 60;

    /// <summary>
    ///     Seconds from the first closed candle that waits to be saved to the save of all that wait by then. The candles of a minute
    ///     come within about a second of one another, so one save takes the minute of the whole market.
    /// </summary>
    public int BatchSeconds { get; init; } = 3;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();

    public ExchangeSyncSettings TInvest { get; init; } = new ExchangeSyncSettings();
}