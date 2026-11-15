namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

/// <summary>
///     A row of <c>instruments</c>: what every instrument has, whatever its exchange.
/// </summary>
internal sealed record InstrumentDb
{
    public Guid Id { get; init; }

    public DateTimeOffset FirstSeenAt { get; init; }

    public DateTimeOffset? DelistedAt { get; init; }
}