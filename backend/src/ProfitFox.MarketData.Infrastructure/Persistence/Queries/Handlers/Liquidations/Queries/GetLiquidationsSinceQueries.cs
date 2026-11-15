namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations.Queries;

internal static class GetLiquidationsSinceQueries
{
    // By time first, as a follower takes them: every instrument of a minute before any of the next one.
    public const string SinceSql = """
                                   SELECT instrument_id AS "InstrumentId",
                                          time AS "Time",
                                          long_volume AS "LongVolume",
                                          long_turnover AS "LongTurnover",
                                          long_count AS "LongCount",
                                          short_volume AS "ShortVolume",
                                          short_turnover AS "ShortTurnover",
                                          short_count AS "ShortCount",
                                          is_valid AS "IsValid"
                                   FROM liquidations
                                   WHERE time >= @Since
                                   ORDER BY time, instrument_id
                                   """;

    public static object Since(DateTimeOffset since)
    {
        return new { Since = since.ToUniversalTime() };
    }
}