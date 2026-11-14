namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

/// <summary>
///     A row of <c>candles</c> as it is read.
/// </summary>
internal sealed record CandleDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset OpenTime { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }

    public decimal? Turnover { get; init; }

    public decimal? BuyVolume { get; init; }
}