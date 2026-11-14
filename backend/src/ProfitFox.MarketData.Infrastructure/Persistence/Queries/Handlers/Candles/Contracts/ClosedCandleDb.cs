using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Contracts;

/// <summary>
///     A row of <c>candles</c> with the exchange of its instrument.
/// </summary>
internal sealed record ClosedCandleDb
{
    public Exchange Exchange { get; init; }

    public Guid InstrumentId { get; init; }

    public DateTimeOffset OpenTime { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }

    public decimal? Turnover { get; init; }

    public decimal? BuyVolume { get; init; }
}