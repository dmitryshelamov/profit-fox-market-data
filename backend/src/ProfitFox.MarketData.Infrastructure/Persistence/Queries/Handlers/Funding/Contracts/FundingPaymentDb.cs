namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;

/// <summary>
///     A row of <c>funding_payments</c> as the query reads it.
/// </summary>
internal sealed record FundingPaymentDb
{
    public DateTimeOffset Time { get; init; }

    public decimal Rate { get; init; }
}