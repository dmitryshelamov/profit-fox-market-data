namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

/// <summary>
///     Rows of <c>candle_series</c> to write: the instruments, and the ends of their covered and of their settled ranges as arrays with
///     the instrument of each range, from which the query builds the multiranges of every row. A series without ranges still gets its row.
/// </summary>
internal sealed record CandleSeriesDb
{
    public Guid[] InstrumentIds { get; init; } = [];

    public Guid[] CoveredInstrumentIds { get; init; } = [];

    public DateTimeOffset[] CoveredFroms { get; init; } = [];

    public DateTimeOffset[] CoveredUntils { get; init; } = [];

    public Guid[] SettledInstrumentIds { get; init; } = [];

    public DateTimeOffset[] SettledFroms { get; init; } = [];

    public DateTimeOffset[] SettledUntils { get; init; } = [];
}