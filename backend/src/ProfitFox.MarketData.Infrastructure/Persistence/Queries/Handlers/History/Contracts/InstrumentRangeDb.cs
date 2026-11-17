namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Contracts;

/// <summary>
///     One covered range of an instrument a read of history asks for. Both ends are null for the one row of an instrument with none.
/// </summary>
internal sealed record InstrumentRangeDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? Until { get; init; }
}