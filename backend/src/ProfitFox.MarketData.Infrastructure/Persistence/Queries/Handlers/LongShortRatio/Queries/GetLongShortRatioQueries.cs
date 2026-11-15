using Dapper;

using ProfitFox.MarketData.Application.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Queries;

internal static class GetLongShortRatioQueries
{
    private const string PointsSql = """
                                     SELECT time AS "Time", long AS "Long", short AS "Short"
                                     FROM long_short_ratio
                                     WHERE instrument_id = @InstrumentId AND time >= @From AND time < @To
                                     ORDER BY time
                                     LIMIT @Limit
                                     """;

    public static CommandDefinition Points(Guid instrumentId, PointWindow window, int limit, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            PointsSql,
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