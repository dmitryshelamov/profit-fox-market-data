using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Contracts.Candles;

/// <summary>
///     A closed minute candle of an instrument as it is stored. The turnover is null where the exchange does not tell it, the buy volume
///     where the sides of the trades are not known.
/// </summary>
public sealed record ClosedCandleInternal(
    Exchange Exchange,
    Guid InstrumentId,
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal? Turnover,
    decimal? BuyVolume);