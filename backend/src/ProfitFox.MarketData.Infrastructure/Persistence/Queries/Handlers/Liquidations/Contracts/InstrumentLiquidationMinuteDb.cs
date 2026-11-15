namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations.Contracts;

/// <summary>
///     A row of <c>liquidations</c> as the feed reads it, with its instrument.
/// </summary>
internal sealed record InstrumentLiquidationMinuteDb
{
    public Guid InstrumentId { get; init; }

    public DateTimeOffset Time { get; init; }

    public decimal LongVolume { get; init; }

    public decimal LongTurnover { get; init; }

    public int LongCount { get; init; }

    public decimal ShortVolume { get; init; }

    public decimal ShortTurnover { get; init; }

    public int ShortCount { get; init; }

    public bool IsValid { get; init; }
}