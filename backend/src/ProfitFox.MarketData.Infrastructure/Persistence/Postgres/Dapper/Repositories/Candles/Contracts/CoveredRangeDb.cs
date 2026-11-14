namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

/// <summary>
///     One range of the <c>covered</c> multirange of a row of <c>candle_series</c>; both ends null for a series that covers nothing.
/// </summary>
internal sealed record CoveredRangeDb
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? Until { get; init; }
}