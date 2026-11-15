namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;

/// <summary>
///     A row of <c>funding_rates</c> as the query reads it.
/// </summary>
internal sealed record FundingRatePointDb
{
    public DateTimeOffset Time { get; init; }

    public decimal Rate { get; init; }
}