namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest.Contracts;

/// <summary>
///     A row of <c>open_interest_series</c>.
/// </summary>
internal sealed record OpenInterestSeriesDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset CoveredFrom { get; init; }

    public DateTimeOffset CoveredUntil { get; init; }
}