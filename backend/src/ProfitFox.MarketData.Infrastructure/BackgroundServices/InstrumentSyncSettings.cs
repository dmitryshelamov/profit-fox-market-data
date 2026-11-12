using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>InstrumentSync</c> section of the settings: how often the catalog syncs and which categories of Bybit it syncs.
/// </summary>
internal sealed class InstrumentSyncSettings
{
    public const string Section = "InstrumentSync";

    /// <summary>
    ///     Minutes between two syncs; the first runs when the app starts.
    /// </summary>
    public int IntervalMinutes { get; init; }

    public BybitCategory[] Categories { get; init; } = [];
}