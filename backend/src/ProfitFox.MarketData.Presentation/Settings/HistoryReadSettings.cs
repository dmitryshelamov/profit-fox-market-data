namespace ProfitFox.MarketData.Presentation.Settings;

/// <summary>
///     The <c>HistoryRead</c> section of the settings: how many calls of history of every feed together read storage at once.
/// </summary>
public sealed class HistoryReadSettings
{
    public const string Section = "HistoryRead";

    /// <summary>
    ///     The most calls of history that read at once, each on a core of the database; the others wait their turn. Read before every
    ///     call, so a change applies from the next place that frees.
    /// </summary>
    public int MaxConcurrentReads { get; init; } = 2;
}