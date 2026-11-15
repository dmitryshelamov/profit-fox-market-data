namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;

/// <summary>
///     A row of <c>funding_payments</c> as the feed reads it, with its instrument.
/// </summary>
internal sealed record InstrumentFundingPaymentDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset Time { get; init; }

    public decimal Rate { get; init; }
}