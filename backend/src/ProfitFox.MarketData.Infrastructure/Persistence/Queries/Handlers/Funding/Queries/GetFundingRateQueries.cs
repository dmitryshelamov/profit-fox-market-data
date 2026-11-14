using Dapper;

using ProfitFox.MarketData.Application.Queries.Funding;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;

internal static class GetFundingRateQueries
{
    private const string PointsSql = """
                                     SELECT time AS "Time", rate AS "Rate"
                                     FROM funding_rates
                                     WHERE instrument_id = @InstrumentId AND time >= @From AND time < @To
                                     ORDER BY time
                                     LIMIT @Limit
                                     """;

    public static CommandDefinition Points(Guid instrumentId, FundingRateWindow window, int limit, CancellationToken cancellationToken)
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