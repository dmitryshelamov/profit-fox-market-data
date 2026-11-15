namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Contracts;

/// <summary>
///     A row of <c>open_interest</c> as the query reads it.
/// </summary>
internal sealed record OpenInterestPointDb
{
    public DateTimeOffset Time { get; init; }

    public decimal Value { get; init; }
}