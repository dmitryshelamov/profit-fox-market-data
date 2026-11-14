namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Contracts;

/// <summary>
///     A candle of an interval as the query builds it from the rows of <c>candles</c>.
/// </summary>
internal sealed record CandleBucketDb
{
    public DateTimeOffset OpenTime { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }

    public decimal Turnover { get; init; }

    public decimal? BuyVolume { get; init; }
}