namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>InstrumentSync</c> section of the settings: how often the catalog syncs and what it syncs of each exchange.
/// </summary>
internal sealed class InstrumentSyncSettings
{
    public const string Section = "InstrumentSync";

    /// <summary>
    ///     Minutes between two syncs; the first runs when the app starts.
    /// </summary>
    public int IntervalMinutes { get; init; }

    public BybitInstrumentSyncSettings Bybit { get; init; } = new BybitInstrumentSyncSettings();

    public TInvestInstrumentSyncSettings TInvest { get; init; } = new TInvestInstrumentSyncSettings();
}