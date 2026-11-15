namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Contracts;

/// <summary>
///     A row of <c>long_short_ratio</c> as the feed reads it, with its instrument.
/// </summary>
internal sealed record InstrumentLongShortRatioDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset Time { get; init; }

    public decimal Long { get; init; }

    public decimal Short { get; init; }
}