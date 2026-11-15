namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

/// <summary>
///     One range of a row of <c>candle_series</c>: of its <c>settled</c> multirange when <see cref="Settled" />, else of its
///     <c>covered</c> one. Both ends are null for the one row of a series that covers nothing.
/// </summary>
internal sealed record SeriesRangeDb
{
    public Guid InstrumentId { get; init; }

    public bool Settled { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? Until { get; init; }
}