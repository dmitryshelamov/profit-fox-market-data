namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Contracts;

/// <summary>
///     A row of <c>open_interest</c> as the feed reads it, with its instrument.
/// </summary>
internal sealed record InstrumentOpenInterestDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset Time { get; init; }

    public decimal Value { get; init; }
}