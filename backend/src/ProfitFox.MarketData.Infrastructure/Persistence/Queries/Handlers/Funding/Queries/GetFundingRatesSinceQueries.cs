namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;

internal static class GetFundingRatesSinceQueries
{
    // By time first, as a follower takes them: every instrument of a time before any of the next one.
    public const string SinceSql = """
                                   SELECT instrument_id AS "InstrumentId", time AS "Time", rate AS "Rate"
                                   FROM funding_rates
                                   WHERE time >= @Since
                                   ORDER BY time, instrument_id
                                   """;

    public static object Since(DateTimeOffset since)
    {
        return new { Since = since.ToUniversalTime() };
    }
}