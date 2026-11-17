namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Queries;

/// <summary>
///     What the reads of history of every kind of data share: the session they read in, the instruments a request names and the
///     coverage a read of rows is given.
/// </summary>
internal static class HistoryQueries
{
    // One core a read, so the loading of history keeps the others, and room for the hash of a day of candles of the whole market, which
    // spills to disk with the default of the server.
    public const string ReadSettingsSql = "SET max_parallel_workers_per_gather = 0; SET work_mem = '256MB';";

    // The first instrument named, by id, that the catalog never kept.
    public const string UnknownSql = """
                                     SELECT n.id
                                     FROM unnest(@InstrumentIds::uuid[]) AS n(id)
                                     WHERE NOT EXISTS (SELECT 1 FROM instruments i WHERE i.id = n.id)
                                     ORDER BY n.id
                                     LIMIT 1
                                     """;

    // The listed instruments a request reads, with their exchange, the table their listing is in: those named, or, when none is, those
    // of the exchanges named, of all when none is either. A delisted instrument is left out, even when named.
    public const string ScopeSql = """
                                   WITH scope AS (
                                       SELECT i.id, l.exchange
                                       FROM instruments i
                                       JOIN (
                                           SELECT instrument_id, 'Bybit' AS exchange FROM bybit_listings
                                           UNION ALL
                                           SELECT instrument_id, 'TInvest' FROM tinvest_shares
                                       ) l ON l.instrument_id = i.id
                                       WHERE i.delisted_at IS NULL
                                         AND CASE
                                                 WHEN cardinality(@InstrumentIds::uuid[]) > 0 THEN i.id = ANY(@InstrumentIds::uuid[])
                                                 ELSE cardinality(@Exchanges::text[]) = 0 OR l.exchange = ANY(@Exchanges::text[])
                                             END
                                   )
                                   """;

    // The coverage a read of rows is given, one multirange an instrument; the ranges come apart, as arrays of the same length.
    public const string CoverageSql = """
                                      WITH coverage AS (
                                          SELECT c.instrument_id, range_agg(tstzrange(c.from_time, c.until_time)) AS ranges
                                          FROM unnest(@InstrumentIds::uuid[], @Froms::timestamptz[], @Untils::timestamptz[])
                                              AS c(instrument_id, from_time, until_time)
                                          GROUP BY c.instrument_id
                                      )
                                      """;

    // The coverage of a series of points, one range a series, cut to the window; one row with null ends for an instrument with none.
    public static string PointCoverageSql(string seriesTable)
    {
        return ScopeSql + $"""

                           SELECT scope.id AS "InstrumentId", lower(r) AS "From", upper(r) AS "Until"
                           FROM scope
                           LEFT JOIN {seriesTable} s ON s.instrument_id = scope.id
                           LEFT JOIN LATERAL unnest(
                               tstzmultirange(tstzrange(s.covered_from, s.covered_until)) * tstzmultirange(tstzrange(@From, @To))
                           ) AS r ON s.instrument_id IS NOT NULL
                           ORDER BY scope.id, lower(r)
                           """;
    }
}