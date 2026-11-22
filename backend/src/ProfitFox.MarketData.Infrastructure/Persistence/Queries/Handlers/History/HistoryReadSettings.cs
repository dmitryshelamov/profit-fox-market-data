namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     The <c>HistoryRead</c> section of the settings: how many calls of history of every feed together read storage at once, and how
///     long each of their statements waits for it.
/// </summary>
public sealed class HistoryReadSettings
{
    public const string Section = "HistoryRead";

    /// <summary>
    ///     The most calls of history that read at once, each on a core of the database; the others wait their turn. Read before every
    ///     call, so a change applies from the next place that frees.
    /// </summary>
    public int MaxConcurrentReads { get; init; } = 2;

    /// <summary>
    ///     How long each statement of a read of history waits for storage to answer, in seconds; the writes and the other reads keep the
    ///     timeout of the connection string. Read at the start of every call, so a change applies from the next one.
    /// </summary>
    public int StatementTimeoutSeconds { get; init; } = 120;
}