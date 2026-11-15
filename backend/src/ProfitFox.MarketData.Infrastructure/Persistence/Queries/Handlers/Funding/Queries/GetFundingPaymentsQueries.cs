using Dapper;

using ProfitFox.MarketData.Application.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;

internal static class GetFundingPaymentsQueries
{
    private const string PaymentsSql = """
                                       SELECT time AS "Time", rate AS "Rate"
                                       FROM funding_payments
                                       WHERE instrument_id = @InstrumentId AND time >= @From AND time < @To
                                       ORDER BY time
                                       LIMIT @Limit
                                       """;

    public static CommandDefinition Payments(Guid instrumentId, PointWindow window, int limit, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            PaymentsSql,
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