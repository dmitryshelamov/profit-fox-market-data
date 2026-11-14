using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     A range of an instrument a read asked for and could not wait for.
/// </summary>
internal sealed record CandleLoadRequest(Guid InstrumentId, MinuteRange Range);