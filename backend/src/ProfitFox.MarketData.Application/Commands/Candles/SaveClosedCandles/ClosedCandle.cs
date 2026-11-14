using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;

/// <summary>
///     The candle of a minute of an instrument that has just closed, as the stream of its exchange told it.
/// </summary>
public sealed record ClosedCandle(Exchange Exchange, Guid InstrumentId, Candle Candle);