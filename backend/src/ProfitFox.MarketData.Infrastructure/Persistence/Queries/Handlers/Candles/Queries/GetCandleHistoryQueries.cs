using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;

internal static class GetCandleHistoryQueries
{
    // Covered on Bybit is what is loaded and settled: a buy volume the archive may still bring would change a minute read before. On
    // T-Invest it is what is loaded: its candles tell their buy volume themselves, and its series are hardly ever settled.
    public const string CoverageSql = HistoryQueries.ScopeSql + """

                                                                SELECT scope.id AS "InstrumentId", lower(r) AS "From", upper(r) AS "Until"
                                                                FROM scope
                                                                LEFT JOIN candle_series s ON s.instrument_id = scope.id
                                                                LEFT JOIN LATERAL unnest(
                                                                    CASE WHEN scope.exchange = 'Bybit' THEN s.covered * s.settled ELSE s.covered END
                                                                    * tstzmultirange(tstzrange(@From, @To))
                                                                ) AS r ON true
                                                                ORDER BY scope.id, lower(r)
                                                                """;

    // The minutes of a day inside the coverage, as they are stored and as the feed tells them live. The time comes first in the order,
    // then the instrument, as Postgres orders its ids: the same on every read.
    public const string MinutesSql = HistoryQueries.CoverageSql + """

                                                                  SELECT l.exchange AS "Exchange", c.instrument_id AS "InstrumentId", c.open_time AS "OpenTime",
                                                                         c.open, c.high, c.low, c.close, c.volume, c.turnover, c.buy_volume AS "BuyVolume"
                                                                  FROM candles c
                                                                  JOIN coverage ON coverage.instrument_id = c.instrument_id
                                                                  JOIN (
                                                                      SELECT instrument_id, 'Bybit' AS exchange FROM bybit_listings
                                                                      UNION ALL
                                                                      SELECT instrument_id, 'TInvest' FROM tinvest_shares
                                                                  ) l ON l.instrument_id = c.instrument_id
                                                                  WHERE c.instrument_id = ANY(@InstrumentIds::uuid[]) AND c.open_time >= @From AND c.open_time < @Until
                                                                    AND coverage.ranges @> c.open_time
                                                                  ORDER BY c.open_time, c.instrument_id
                                                                  """;

    // The candles of the interval of a day whose minutes the coverage holds all, built as GetCandlesQueries.Buckets builds them for the
    // REST API: a candle with a minute outside the coverage would tell less than its trades. Every interval divides a day, so no candle
    // crosses the end of the day the statement reads. Hashed, not sorted, by instrument: a day of the whole market is two million minutes.
    public const string BucketsSql = HistoryQueries.CoverageSql + """
                                                                  ,
                                                                  buckets AS (
                                                                      SELECT c.instrument_id,
                                                                             time_bucket(@BucketSeconds * interval '1 second', c.open_time) AS open_time,
                                                                             first(c.open, c.open_time) AS open,
                                                                             max(c.high) AS high,
                                                                             min(c.low) AS low,
                                                                             last(c.close, c.open_time) AS close,
                                                                             sum(c.volume) AS volume,
                                                                             sum(coalesce(c.turnover, c.volume * (c.high + c.low + c.close) / 3)) AS turnover,
                                                                             CASE WHEN count(c.buy_volume) = count(*) THEN sum(c.buy_volume) END AS buy_volume
                                                                      FROM candles c
                                                                      WHERE c.instrument_id = ANY(@InstrumentIds::uuid[]) AND c.open_time >= @From AND c.open_time < @Until
                                                                      GROUP BY 1, 2
                                                                  )
                                                                  SELECT l.exchange AS "Exchange", b.instrument_id AS "InstrumentId", b.open_time AS "OpenTime",
                                                                         b.open, b.high, b.low, b.close, b.volume, b.turnover, b.buy_volume AS "BuyVolume"
                                                                  FROM buckets b
                                                                  JOIN coverage ON coverage.instrument_id = b.instrument_id
                                                                      AND coverage.ranges @> tstzrange(b.open_time, b.open_time + @BucketSeconds * interval '1 second')
                                                                  JOIN (
                                                                      SELECT instrument_id, 'Bybit' AS exchange FROM bybit_listings
                                                                      UNION ALL
                                                                      SELECT instrument_id, 'TInvest' FROM tinvest_shares
                                                                  ) l ON l.instrument_id = b.instrument_id
                                                                  ORDER BY b.open_time, b.instrument_id
                                                                  """;

    // The parameters alone, with the statement beside them: Dapper reads row by row only from a statement and its parameters, not from
    // a command definition.
    public static object Day(HistoryDay day, CandleInterval interval)
    {
        return new
        {
            day.InstrumentIds,
            Froms = day.Froms.Select(from => from.ToUniversalTime()).ToArray(),
            Untils = day.Untils.Select(until => until.ToUniversalTime()).ToArray(),
            From = day.From.ToUniversalTime(),
            Until = day.Until.ToUniversalTime(),
            BucketSeconds = (int)interval.Length().TotalSeconds
        };
    }
}