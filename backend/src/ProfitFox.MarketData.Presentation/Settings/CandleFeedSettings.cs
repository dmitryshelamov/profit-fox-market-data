namespace ProfitFox.MarketData.Presentation.Settings;

/// <summary>
///     The <c>CandleFeed</c> section of the settings: how far back a subscriber of the feed of closed candles may catch up from storage.
/// </summary>
public sealed class CandleFeedSettings
{
    public const string Section = "CandleFeed";

    /// <summary>
    ///     The most hours back from now a subscription may start from: a day of the whole market is about two million candles.
    /// </summary>
    public int MaxCatchUpHours { get; init; } = 24;
}