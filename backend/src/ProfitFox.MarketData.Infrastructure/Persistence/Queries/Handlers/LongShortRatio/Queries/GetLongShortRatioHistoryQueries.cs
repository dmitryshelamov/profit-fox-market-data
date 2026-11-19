using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Queries;

internal static class GetLongShortRatioHistoryQueries
{
    // Covered is the one range of the series of the instrument: inside it, every point the exchange has is stored.
    public static readonly string CoverageSql = HistoryQueries.PointCoverageSql("long_short_ratio_series");

    // The points of the long/short ratio of a part of the window inside the coverage. The time comes first in the order, then the
    // instrument, as Postgres orders its ids: the same on every read.
    public const string PointsSql = HistoryQueries.CoverageSql + """

                                                                 SELECT p.instrument_id AS "InstrumentId", p.time AS "Time", p.long AS "Long", p.short AS "Short"
                                                                 FROM long_short_ratio p
                                                                 JOIN coverage ON coverage.instrument_id = p.instrument_id
                                                                 WHERE p.instrument_id = ANY(@InstrumentIds::uuid[]) AND p.time >= @From AND p.time < @Until
                                                                   AND coverage.ranges @> p.time
                                                                 ORDER BY p.time, p.instrument_id
                                                                 """;

    // The parameters of the statement of a part of the read.
    public static object Period(HistoryPeriod period)
    {
        return new
        {
            period.InstrumentIds,
            Froms = period.Froms.Select(from => from.ToUniversalTime()).ToArray(),
            Untils = period.Untils.Select(until => until.ToUniversalTime()).ToArray(),
            From = period.From.ToUniversalTime(),
            Until = period.Until.ToUniversalTime()
        };
    }
}