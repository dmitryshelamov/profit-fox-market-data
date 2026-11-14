using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     A minute candle of an instrument as it is told live. <see cref="Closed" /> is the candle of a minute that ended, saved and final
///     but for a later word of its buy volume; one not closed is the candle of the minute that goes on, as far as its trades went, and
///     is told again as they go on.
/// </summary>
public sealed record LiveCandle(Exchange Exchange, Guid InstrumentId, Candle Candle, bool Closed);