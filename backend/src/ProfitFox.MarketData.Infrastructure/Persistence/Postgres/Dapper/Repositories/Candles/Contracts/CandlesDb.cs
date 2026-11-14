namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

/// <summary>
///     Rows of <c>candles</c> of one instrument to write, one array per column, so a page of candles goes in one statement.
/// </summary>
internal sealed record CandlesDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset[] OpenTimes { get; init; } = [];

    public decimal[] Opens { get; init; } = [];

    public decimal[] Highs { get; init; } = [];

    public decimal[] Lows { get; init; } = [];

    public decimal[] Closes { get; init; } = [];

    public decimal[] Volumes { get; init; } = [];

    public decimal?[] Turnovers { get; init; } = [];
}