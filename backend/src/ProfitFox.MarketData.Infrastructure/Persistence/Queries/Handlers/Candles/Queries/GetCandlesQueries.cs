using Dapper;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;

internal static class GetCandlesQueries
{
    // The stored minutes of the range in buckets of the interval. The turnover an exchange does not tell (T-Invest) is estimated minute by
    // minute: the volume times the mean of the high, the low and the close. The buy volume of a bucket is known only when the one of
    // every its minute is: a sum over a part of them would read as a bucket sold into.
    private const string BucketsSql = """
                                      SELECT time_bucket(@BucketSeconds * interval '1 second', open_time) AS "OpenTime",
                                             first(open, open_time) AS "Open",
                                             max(high) AS "High",
                                             min(low) AS "Low",
                                             last(close, open_time) AS "Close",
                                             sum(volume) AS "Volume",
                                             sum(coalesce(turnover, volume * (high + low + close) / 3)) AS "Turnover",
                                             CASE WHEN count(buy_volume) = count(*) THEN sum(buy_volume) END AS "BuyVolume"
                                      FROM candles
                                      WHERE instrument_id = @InstrumentId AND open_time >= @From AND open_time < @Until
                                      GROUP BY 1
                                      ORDER BY 1
                                      """;

    public static CommandDefinition Buckets(Guid instrumentId, MinuteRange range, TimeSpan bucket, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            BucketsSql,
            new
            {
                InstrumentId = instrumentId,
                BucketSeconds = (int)bucket.TotalSeconds,
                From = range.From.ToUniversalTime(),
                Until = range.Until.ToUniversalTime()
            },
            cancellationToken: cancellationToken);
    }
}