namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding.Contracts;

/// <summary>
///     A row of <c>funding_payment_series</c>.
/// </summary>
internal sealed record FundingPaymentSeriesDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset CoveredFrom { get; init; }

    public DateTimeOffset CoveredUntil { get; init; }
}