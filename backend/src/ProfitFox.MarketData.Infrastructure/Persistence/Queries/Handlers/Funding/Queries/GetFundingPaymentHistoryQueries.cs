using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;

internal static class GetFundingPaymentHistoryQueries
{
    // Covered is the one range of the series of the instrument: inside it, every point the exchange has is stored.
    public static readonly string CoverageSql = HistoryQueries.PointCoverageSql("funding_payment_series");

    // The funding payments of a day inside the coverage. The time comes first in the order, then the instrument, as Postgres orders its ids: the
    // same on every read.
    public const string PointsSql = HistoryQueries.CoverageSql + """

                                                                 SELECT p.instrument_id AS "InstrumentId", p.time AS "Time", p.rate AS "Rate"
                                                                 FROM funding_payments p
                                                                 JOIN coverage ON coverage.instrument_id = p.instrument_id
                                                                 WHERE p.instrument_id = ANY(@InstrumentIds::uuid[]) AND p.time >= @From AND p.time < @Until
                                                                   AND coverage.ranges @> p.time
                                                                 ORDER BY p.time, p.instrument_id
                                                                 """;

    // The parameters alone, with the statement beside them: Dapper reads row by row only from a statement and its parameters, not from
    // a command definition.
    public static object Day(HistoryDay day)
    {
        return new
        {
            day.InstrumentIds,
            Froms = day.Froms.Select(from => from.ToUniversalTime()).ToArray(),
            Untils = day.Untils.Select(until => until.ToUniversalTime()).ToArray(),
            From = day.From.ToUniversalTime(),
            Until = day.Until.ToUniversalTime()
        };
    }
}