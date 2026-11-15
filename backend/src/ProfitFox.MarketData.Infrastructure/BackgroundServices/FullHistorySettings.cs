using ProfitFox.MarketData.Application.Contracts.Instruments;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>FullHistory</c> section of the settings: the instruments whose whole history the loadings of candles, buy volumes, open
///     interest, funding payments and the long/short ratio load, back to when they started to trade, instead of the days of history of
///     each loading.
/// </summary>
internal sealed class FullHistorySettings
{
    public const string Section = "FullHistory";

    public BybitFullHistorySettings Bybit { get; init; } = new BybitFullHistorySettings();

    /// <summary>
    ///     Whether the whole history of the instrument is loaded.
    /// </summary>
    public bool Covers(InstrumentInternal instrument)
    {
        return instrument.Listing is BybitListedInstrumentInternal listing && Bybit.Symbols.Contains(listing.Symbol, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Where the history of the instrument is loaded from: for a whole history, the launch of a contract of Bybit, or the Unix epoch,
    ///     before any exchange traded, for a spot pair, which tells no launch; for any other instrument, <paramref name="historyFrom" />.
    /// </summary>
    public DateTimeOffset HistoryFrom(InstrumentInternal instrument, DateTimeOffset historyFrom)
    {
        if (!Covers(instrument))
        {
            return historyFrom;
        }

        return instrument.Listing is BybitListedInstrumentInternal { LaunchTime: { } launchTime } ? launchTime : DateTimeOffset.UnixEpoch;
    }
}