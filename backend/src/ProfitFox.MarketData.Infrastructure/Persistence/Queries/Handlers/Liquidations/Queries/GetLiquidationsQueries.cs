using Dapper;

using ProfitFox.MarketData.Application.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations.Queries;

internal static class GetLiquidationsQueries
{
    private const string MinutesSql = """
                                      SELECT time AS "Time",
                                             long_volume AS "LongVolume",
                                             long_turnover AS "LongTurnover",
                                             long_count AS "LongCount",
                                             short_volume AS "ShortVolume",
                                             short_turnover AS "ShortTurnover",
                                             short_count AS "ShortCount",
                                             is_valid AS "IsValid"
                                      FROM liquidations
                                      WHERE instrument_id = @InstrumentId AND time >= @From AND time < @To
                                      ORDER BY time
                                      LIMIT @Limit
                                      """;

    public static CommandDefinition Minutes(Guid instrumentId, PointWindow window, int limit, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            MinutesSql,
            new
            {
                InstrumentId = instrumentId,
                From = window.From.ToUniversalTime(),
                To = window.To.ToUniversalTime(),
                Limit = limit
            },
            cancellationToken: cancellationToken);
    }
}