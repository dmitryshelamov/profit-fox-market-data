using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     A window of an instrument a read asked for and could not load in time.
/// </summary>
internal sealed record CandleLoadRequest(Guid InstrumentId, MinuteRange Range);