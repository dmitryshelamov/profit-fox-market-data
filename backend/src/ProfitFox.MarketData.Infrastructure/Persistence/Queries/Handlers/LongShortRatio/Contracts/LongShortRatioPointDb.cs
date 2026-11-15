namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Contracts;

/// <summary>
///     A row of <c>long_short_ratio</c> as the query reads it.
/// </summary>
internal sealed record LongShortRatioPointDb
{
    public DateTimeOffset Time { get; init; }

    public decimal Long { get; init; }

    public decimal Short { get; init; }
}