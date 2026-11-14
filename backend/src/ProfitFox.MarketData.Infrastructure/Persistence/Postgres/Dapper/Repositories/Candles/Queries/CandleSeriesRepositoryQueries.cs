using System.Data;

using Dapper;

using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles.Queries;

/// <summary>
///     A series is a row of <c>candle_series</c>, its covered ranges one multirange; its candles are rows of <c>candles</c>. Arrays go to
///     Postgres as they are, so a page of candles is one statement.
/// </summary>
internal static class CandleSeriesRepositoryQueries
{
    // No row for a series never saved; one row with null ends for a series that covers nothing.
    private const string CoveredSql = """
                                      SELECT lower(r) AS "From", upper(r) AS "Until"
                                      FROM candle_series s
                                      LEFT JOIN LATERAL unnest(s.covered) AS r ON true
                                      WHERE s.instrument_id = @InstrumentId
                                      ORDER BY 1
                                      """;

    // The saved ranges replace the stored ones: the series read them before it took more.
    private const string UpsertSeriesSql = """
                                           INSERT INTO candle_series (instrument_id, covered)
                                           SELECT @InstrumentId, coalesce(range_agg(tstzrange(r.from_time, r.until_time, '[)')), '{}'::tstzmultirange)
                                           FROM unnest(@CoveredFroms::timestamptz[], @CoveredUntils::timestamptz[]) AS r(from_time, until_time)
                                           ON CONFLICT (instrument_id) DO UPDATE SET covered = EXCLUDED.covered
                                           """;

    // A candle of a minute already stored takes the values the exchange gives now.
    private const string UpsertCandlesSql = """
                                            INSERT INTO candles (instrument_id, open_time, open, high, low, close, volume, turnover)
                                            SELECT @InstrumentId, c.open_time, c.open, c.high, c.low, c.close, c.volume, c.turnover
                                            FROM unnest(
                                                @OpenTimes::timestamptz[], @Opens::numeric[], @Highs::numeric[], @Lows::numeric[],
                                                @Closes::numeric[], @Volumes::numeric[], @Turnovers::numeric[]
                                            ) AS c(open_time, open, high, low, close, volume, turnover)
                                            ON CONFLICT (instrument_id, open_time) DO UPDATE SET
                                                open = EXCLUDED.open,
                                                high = EXCLUDED.high,
                                                low = EXCLUDED.low,
                                                close = EXCLUDED.close,
                                                volume = EXCLUDED.volume,
                                                turnover = EXCLUDED.turnover
                                            """;

    public static CommandDefinition Covered(Guid instrumentId, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(CoveredSql, new { InstrumentId = instrumentId }, transaction, cancellationToken: cancellationToken);
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