namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

/// <summary>
///     A row of <c>tinvest_shares</c>, the listing of a share of T-Invest.
/// </summary>
internal sealed record TInvestShareDb
{
    public Guid InstrumentId { get; init; }

    public required string Uid { get; init; }

    public required string Ticker { get; init; }

    public decimal TickSize { get; init; }

    public decimal QuantityStep { get; init; }

    public decimal MinQuantity { get; init; }
}