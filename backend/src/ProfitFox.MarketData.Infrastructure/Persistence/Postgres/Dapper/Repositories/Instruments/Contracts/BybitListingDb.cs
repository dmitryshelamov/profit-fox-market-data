namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

/// <summary>
///     A row of <c>bybit_listings</c>, the listing of an instrument of Bybit; enums are the names of their members.
/// </summary>
internal sealed record BybitListingDb
{
    public Guid InstrumentId { get; init; }

    public required string Category { get; init; }

    public required string Symbol { get; init; }

    public required string BaseAsset { get; init; }

    public required string QuoteAsset { get; init; }

    public required string Status { get; init; }

    public decimal TickSize { get; init; }

    public decimal QuantityStep { get; init; }

    public decimal MinQuantity { get; init; }

    public decimal? MinAmount { get; init; }

    public string? ContractType { get; init; }

    public DateTimeOffset? LaunchTime { get; init; }
}