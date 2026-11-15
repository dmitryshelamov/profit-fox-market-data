using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;

internal static class GetClosedCandlesQueries
{
    // The exchange of an instrument is the table its listing is in. The time comes first in the order, so candles of every instrument
    // come minute by minute, as they did live.
    internal const string SinceSql = """
                                     SELECT l.exchange AS "Exchange", c.instrument_id AS "InstrumentId", c.open_time AS "OpenTime", c.open, c.high,
                                            c.low, c.close, c.volume, c.turnover, c.buy_volume AS "BuyVolume"
                                     FROM candles c
                                     JOIN (
                                         SELECT instrument_id, 'Bybit' AS exchange FROM bybit_listings
                                         UNION ALL
                                         SELECT instrument_id, 'TInvest' FROM tinvest_shares
                                     ) l ON l.instrument_id = c.instrument_id
                                     WHERE c.open_time >= @Since AND l.exchange = ANY(@Exchanges)
                                     ORDER BY c.open_time, c.instrument_id
                                     """;

    // The parameters alone, with the statement beside them: Dapper reads row by row only from a statement and its parameters, not from
    // a command definition.
    internal static object Since(DateTimeOffset since, IReadOnlyCollection<Exchange> exchanges)
    {
        return new { Since = since.ToUniversalTime(), Exchanges = exchanges.Select(exchange => exchange.ToString()).ToArray() };
    }
}