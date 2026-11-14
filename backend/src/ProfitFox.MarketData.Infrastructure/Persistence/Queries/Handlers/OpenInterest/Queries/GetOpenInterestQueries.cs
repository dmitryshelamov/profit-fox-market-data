using Dapper;

using ProfitFox.MarketData.Application.Queries.OpenInterest;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Queries;

internal static class GetOpenInterestQueries
{
    private const string PointsSql = """
                                     SELECT time AS "Time", value AS "Value"
                                     FROM open_interest
                                     WHERE instrument_id = @InstrumentId AND time >= @From AND time < @To
                                     ORDER BY time
                                     LIMIT @Limit
                                     """;

    public static CommandDefinition Points(Guid instrumentId, OpenInterestWindow window, int limit, CancellationToken cancellationToken)
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