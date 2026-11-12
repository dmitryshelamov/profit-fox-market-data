namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

/// <summary>
///     A row of <c>instruments</c>; enums are the names of their members.
/// </summary>
internal sealed record InstrumentDb
{
    public Guid Id { get; init; }

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

    public DateTimeOffset FirstSeenAt { get; init; }

    public DateTimeOffset? DelistedAt { get; init; }
}