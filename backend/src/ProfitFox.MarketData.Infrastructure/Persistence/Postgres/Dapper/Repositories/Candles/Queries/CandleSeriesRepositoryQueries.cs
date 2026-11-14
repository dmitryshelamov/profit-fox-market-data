using System.Data;

using Dapper;

using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Queries;

/// <summary>
///     A series is a row of <c>candle_series</c>, its covered ranges one multirange; its candles are rows of <c>candles</c>. Arrays go to
///     Postgres as they are, so the candles of a page, or of many series, are one statement.
/// </summary>
internal static class CandleSeriesRepositoryQueries
{
    // No row for a series never saved; one row with null ends for a series that covers nothing.
    private const string CoveredSql = """
                                      SELECT s.instrument_id AS "InstrumentId", lower(r) AS "From", upper(r) AS "Until"
                                      FROM candle_series s
                                      LEFT JOIN LATERAL unnest(s.covered) AS r ON true
                                      WHERE s.instrument_id = ANY(@InstrumentIds)
                                      ORDER BY 1, 2
                                      """;

    // The saved ranges are added to the stored ones: two writers of one instrument (the background loading, a read, the stream) each
    // keep what the others covered since they read the series. A series without ranges joins none and gets an empty multirange.
    private const string UpsertSeriesSql = """
                                           INSERT INTO candle_series (instrument_id, covered)
                                           SELECT s.instrument_id,
                                                  coalesce(
                                                      range_agg(tstzrange(r.from_time, r.until_time, '[)')) FILTER (WHERE r.instrument_id IS NOT NULL),
                                                      '{}'::tstzmultirange)
                                           FROM unnest(@InstrumentIds::uuid[]) AS s(instrument_id)
                                           LEFT JOIN unnest(
                                               @CoveredInstrumentIds::uuid[], @CoveredFroms::timestamptz[], @CoveredUntils::timestamptz[]
                                           ) AS r(instrument_id, from_time, until_time) ON r.instrument_id = s.instrument_id
                                           GROUP BY s.instrument_id
                                           ON CONFLICT (instrument_id) DO UPDATE SET covered = candle_series.covered + EXCLUDED.covered
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

    public static CommandDefinition Covered(IReadOnlyCollection<Guid> instrumentIds, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(CoveredSql, new { InstrumentIds = instrumentIds.ToArray() }, transaction, cancellationToken: cancellationToken);
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