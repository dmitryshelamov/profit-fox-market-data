using Dapper;

using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Liquidations.Queries;

internal static class LiquidationRepositoryQueries
{
    // The changed rows as storage holds them, by time first, as a follower takes them.
    private const string ChangedSql = """
                                      SELECT instrument_id AS "InstrumentId",
                                             time AS "Time",
                                             long_volume AS "LongVolume",
                                             long_turnover AS "LongTurnover",
                                             long_count AS "LongCount",
                                             short_volume AS "ShortVolume",
                                             short_turnover AS "ShortTurnover",
                                             short_count AS "ShortCount",
                                             is_valid AS "IsValid"
                                      FROM changed
                                      ORDER BY time, instrument_id
                                      """;

    // The minutes go as one array per column, so the sums of the whole market are one statement. A stored minute takes the sums on top
    // of its own and keeps its mark; a new one is valid.
    private const string AddSql = $"""
                                   WITH changed AS (
                                       INSERT INTO liquidations (
                                           instrument_id, time, long_volume, long_turnover, long_count, short_volume, short_turnover, short_count, is_valid)
                                       SELECT m.*, TRUE
                                       FROM unnest(
                                           @InstrumentIds::uuid[],
                                           @Times::timestamptz[],
                                           @LongVolumes::numeric[],
                                           @LongTurnovers::numeric[],
                                           @LongCounts::integer[],
                                           @ShortVolumes::numeric[],
                                           @ShortTurnovers::numeric[],
                                           @ShortCounts::integer[]) AS m
                                       ON CONFLICT (instrument_id, time) DO UPDATE SET
                                           long_volume = liquidations.long_volume + EXCLUDED.long_volume,
                                           long_turnover = liquidations.long_turnover + EXCLUDED.long_turnover,
                                           long_count = liquidations.long_count + EXCLUDED.long_count,
                                           short_volume = liquidations.short_volume + EXCLUDED.short_volume,
                                           short_turnover = liquidations.short_turnover + EXCLUDED.short_turnover,
                                           short_count = liquidations.short_count + EXCLUDED.short_count
                                       RETURNING *
                                   )
                                   {ChangedSql}
                                   """;

    // A row for every minute of every instrument, made by the database: a downtime of the whole market is too many rows to send. A
    // stored minute keeps its sums and loses only its mark; one that is not valid already is left as it is and is not given back.
    private const string InvalidateSql = $"""
                                          WITH changed AS (
                                              INSERT INTO liquidations (
                                                  instrument_id, time, long_volume, long_turnover, long_count, short_volume, short_turnover, short_count, is_valid)
                                              SELECT instrument_id, minute, 0, 0, 0, 0, 0, 0, FALSE
                                              FROM unnest(@InstrumentIds::uuid[]) AS instrument_id
                                              CROSS JOIN generate_series(@From::timestamptz, @Until::timestamptz, INTERVAL '1 minute') AS minute
                                              ON CONFLICT (instrument_id, time) DO UPDATE SET
                                                  is_valid = FALSE
                                              WHERE liquidations.is_valid
                                              RETURNING *
                                          )
                                          {ChangedSql}
                                          """;

    private const string LastTimeSql = "SELECT max(time) FROM liquidations";

    public static CommandDefinition Add(IReadOnlyCollection<LiquidationMinute> minutes, CancellationToken cancellationToken)
    {
        var columns = new
        {
            InstrumentIds = minutes.Select(minute => minute.InstrumentId).ToArray(),
            Times = minutes.Select(minute => minute.Time.ToUniversalTime()).ToArray(),
            LongVolumes = minutes.Select(minute => minute.Longs.Volume).ToArray(),
            LongTurnovers = minutes.Select(minute => minute.Longs.Turnover).ToArray(),
            LongCounts = minutes.Select(minute => minute.Longs.Count).ToArray(),
            ShortVolumes = minutes.Select(minute => minute.Shorts.Volume).ToArray(),
            ShortTurnovers = minutes.Select(minute => minute.Shorts.Turnover).ToArray(),
            ShortCounts = minutes.Select(minute => minute.Shorts.Count).ToArray()
        };

        return new CommandDefinition(AddSql, columns, cancellationToken: cancellationToken);
    }

    public static CommandDefinition Invalidate(
        IReadOnlyCollection<Guid> instrumentIds,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            InvalidateSql,
            new { InstrumentIds = instrumentIds.ToArray(), From = from.ToUniversalTime(), Until = until.ToUniversalTime() },
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition LastTime(CancellationToken cancellationToken)
    {
        return new CommandDefinition(LastTimeSql, cancellationToken: cancellationToken);
    }
}