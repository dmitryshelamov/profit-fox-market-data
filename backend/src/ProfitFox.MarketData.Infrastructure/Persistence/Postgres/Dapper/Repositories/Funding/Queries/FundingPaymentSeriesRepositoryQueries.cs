using System.Data;

using Dapper;

using ProfitFox.MarketData.Domain.Entities.Funding;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding.Queries;

/// <summary>
///     A series is a row of <c>funding_payment_series</c>, its payments are rows of <c>funding_payments</c>.
/// </summary>
internal static class FundingPaymentSeriesRepositoryQueries
{
    private const string SeriesSql = """
                                     SELECT instrument_id AS "InstrumentId", covered_from AS "CoveredFrom", covered_until AS "CoveredUntil"
                                     FROM funding_payment_series
                                     WHERE instrument_id = @InstrumentId
                                     """;

    private const string UpsertSeriesSql = """
                                           INSERT INTO funding_payment_series (instrument_id, covered_from, covered_until)
                                           VALUES (@InstrumentId, @CoveredFrom, @CoveredUntil)
                                           ON CONFLICT (instrument_id) DO UPDATE SET
                                               covered_from = EXCLUDED.covered_from,
                                               covered_until = EXCLUDED.covered_until
                                           """;

    // The payments go as one array per column, so a page is one statement. A stored payment of the same time takes the saved rate.
    private const string UpsertPaymentsSql = """
                                             INSERT INTO funding_payments (instrument_id, time, rate)
                                             SELECT @InstrumentId, p.time, p.rate
                                             FROM unnest(@Times::timestamptz[], @Rates::numeric[]) AS p(time, rate)
                                             ON CONFLICT (instrument_id, time) DO UPDATE SET
                                                 rate = EXCLUDED.rate
                                             """;

    public static CommandDefinition Series(Guid instrumentId, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(SeriesSql, new { InstrumentId = instrumentId }, transaction, cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertSeries(FundingPaymentSeries series, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            UpsertSeriesSql,
            new
            {
                series.InstrumentId,
                CoveredFrom = series.CoveredFrom.ToUniversalTime(),
                CoveredUntil = series.CoveredUntil.ToUniversalTime()
            },
            transaction,
            cancellationToken: cancellationToken);
    }

    public static CommandDefinition UpsertPayments(FundingPaymentSeries series, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            UpsertPaymentsSql,
            new
            {
                series.InstrumentId,
                Times = series.NewPayments.Select(payment => payment.Time.ToUniversalTime()).ToArray(),
                Rates = series.NewPayments.Select(payment => payment.Rate).ToArray()
            },
            transaction,
            cancellationToken: cancellationToken);
    }
}