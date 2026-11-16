namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>CandleSync</c> section of the settings: how far back the candles of the catalog are loaded, how many instruments of an
///     exchange load at a time, how long a caught-up exchange waits before its next pass, and which exchanges load.
/// </summary>
internal sealed class CandleSyncSettings
{
    public const string Section = "CandleSync";

    /// <summary>
    ///     Days of history back from now that every listed instrument loads, not before it started to trade.
    /// </summary>
    public int HistoryDays { get; init; } = 365;

    /// <summary>
    ///     Seconds an exchange waits before its next pass once a pass found nothing left to load.
    /// </summary>
    public int IdleSeconds { get; init; } = 60;

    /// <summary>
    ///     How many instruments of an exchange load a page at the same time; each waits for its page before it asks for another.
    /// </summary>
    public int Parallelism { get; init; } = 4;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();

    public ExchangeSyncSettings TInvest { get; init; } = new ExchangeSyncSettings();
}