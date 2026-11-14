using System.Data;

using Dapper;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Queries;

/// <summary>
///     A series is a row of <c>candle_series</c>, its covered ranges one multirange and its settled ranges another; its candles are rows
///     of <c>candles</c>. Arrays go to Postgres as they are, so the candles of a page, or of many series, are one statement.
/// </summary>
internal static class CandleSeriesRepositoryQueries
{
    // No row for a series never saved; one row with null ends for a series that covers nothing. Its settled ranges follow the covered.
    private const string RangesSql = """
                                     SELECT s.instrument_id AS "InstrumentId", false AS "Settled", lower(r) AS "From", upper(r) AS "Until"
                                     FROM candle_series s
                                     LEFT JOIN LATERAL unnest(s.covered) AS r ON true
                                     WHERE s.instrument_id = ANY(@InstrumentIds)
                                     UNION ALL
                                     SELECT s.instrument_id, true, lower(r), upper(r)
                                     FROM candle_series s
                                     CROSS JOIN LATERAL unnest(s.settled) AS r
                                     WHERE s.instrument_id = ANY(@InstrumentIds)
                                     ORDER BY 1, 2, 3
                                     """;

    private const string CandlesWithoutBuyVolumeSql = """
                                                      SELECT open_time AS "OpenTime", open, high, low, close, volume, turnover,
                                                             buy_volume AS "BuyVolume"
                                                      FROM candles
                                                      WHERE instrument_id = @InstrumentId AND open_time >= @From AND open_time < @Until
                                                        AND buy_volume IS NULL
                                                      ORDER BY open_time
                                                      """;

    // The saved ranges are added to the stored ones: two writers of one instrument (the background loading, a read, the stream) each
    // keep what the others covered and settled since they read the series. A series without ranges joins none and gets empty multiranges.
    private const string UpsertSeriesSql = """
                                           WITH covered AS (
                                               SELECT r.instrument_id, range_agg(tstzrange(r.from_time, r.until_time, '[)')) AS ranges
                                               FROM unnest(
                                                   @CoveredInstrumentIds::uuid[], @CoveredFroms::timestamptz[], @CoveredUntils::timestamptz[]
                                               ) AS r(instrument_id, from_time, until_time)
                                               GROUP BY r.instrument_id
                                           ), settled AS (
                                               SELECT r.instrument_id, range_agg(tstzrange(r.from_time, r.until_time, '[)')) AS ranges
                                               FROM unnest(
                                                   @SettledInstrumentIds::uuid[], @SettledFroms::timestamptz[], @SettledUntils::timestamptz[]
                                               ) AS r(instrument_id, from_time, until_time)
                                               GROUP BY r.instrument_id
                                           )
                                           INSERT INTO candle_series (instrument_id, covered, settled)
                                           SELECT s.instrument_id,
                                                  coalesce(covered.ranges, '{}'::tstzmultirange),
                                                  coalesce(settled.ranges, '{}'::tstzmultirange)
                                           FROM unnest(@InstrumentIds::uuid[]) AS s(instrument_id)
                                           LEFT JOIN covered ON covered.instrument_id = s.instrument_id
                                           LEFT JOIN settled ON settled.instrument_id = s.instrument_id
                                           ON CONFLICT (instrument_id) DO UPDATE SET
                                               covered = candle_series.covered + EXCLUDED.covered,
                                               settled = candle_series.settled + EXCLUDED.settled
                                           """;

    // A candle of a minute already stored takes the values the exchange gives now. Its buy volume stays when the new candle knows none:
    // a source that tells the sides of the trades and one that does not write the same minute, in either order.
    private const string UpsertCandlesSql = """
                                            INSERT INTO candles (instrument_id, open_time, open, high, low, close, volume, turnover, buy_volume)
                                            SELECT c.instrument_id, c.open_time, c.open, c.high, c.low, c.close, c.volume, c.turnover, c.buy_volume
                                            FROM unnest(
                                                @InstrumentIds::uuid[], @OpenTimes::timestamptz[], @Opens::numeric[], @Highs::numeric[],
                                                @Lows::numeric[], @Closes::numeric[], @Volumes::numeric[], @Turnovers::numeric[],
                                                @BuyVolumes::numeric[]
                                            ) AS c(instrument_id, open_time, open, high, low, close, volume, turnover, buy_volume)
                                            ON CONFLICT (instrument_id, open_time) DO UPDATE SET
                                                open = EXCLUDED.open,
                                                high = EXCLUDED.high,
                                                low = EXCLUDED.low,
                                                close = EXCLUDED.close,
                                                volume = EXCLUDED.volume,
                                                turnover = EXCLUDED.turnover,
                                                buy_volume = coalesce(EXCLUDED.buy_volume, candles.buy_volume)
                                            """;

    public static CommandDefinition Ranges(IReadOnlyCollection<Guid> instrumentIds, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(RangesSql, new { InstrumentIds = instrumentIds.ToArray() }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition CandlesWithoutBuyVolume(
        Guid instrumentId,
        MinuteRange range,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            CandlesWithoutBuyVolumeSql,
            new { InstrumentId = instrumentId, From = range.From.ToUniversalTime(), Until = range.Until.ToUniversalTime() },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertSeries(CandleSeriesDb row, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertSeriesSql, row, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertCandles(CandlesDb rows, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(UpsertCandlesSql, rows, transaction, cancellationToken: cancellationToken);
    }
}