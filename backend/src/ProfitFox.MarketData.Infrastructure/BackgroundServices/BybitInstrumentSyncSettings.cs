using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>InstrumentSync:Bybit</c> section: whether the catalog syncs Bybit and which of its categories.
/// </summary>
internal sealed class BybitInstrumentSyncSettings
{
    public bool Enabled { get; init; }

    public BybitCategory[] Categories { get; init; } = [];
}