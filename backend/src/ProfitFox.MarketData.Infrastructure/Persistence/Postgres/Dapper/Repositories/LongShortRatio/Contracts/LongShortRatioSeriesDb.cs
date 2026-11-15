namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.LongShortRatio.Contracts;

/// <summary>
///     A row of <c>long_short_ratio_series</c>.
/// </summary>
internal sealed record LongShortRatioSeriesDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset CoveredFrom { get; init; }

    public DateTimeOffset CoveredUntil { get; init; }
}