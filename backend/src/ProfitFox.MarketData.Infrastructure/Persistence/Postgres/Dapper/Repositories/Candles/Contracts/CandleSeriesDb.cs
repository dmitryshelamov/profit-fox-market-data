namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

/// <summary>
///     A row of <c>candle_series</c> to write: the ends of its covered ranges as two arrays, from which the query builds the multirange.
/// </summary>
internal sealed record CandleSeriesDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset[] CoveredFroms { get; init; } = [];

    public DateTimeOffset[] CoveredUntils { get; init; } = [];
}