namespace ProfitFox.MarketData.Application.Contracts.LongShortRatio;

/// <summary>
///     The long/short ratio of an instrument at a time, as it is stored: what the feed of the long/short ratio tells, of the whole
///     market, so it names its instrument.
/// </summary>
public sealed record InstrumentLongShortRatioInternal(Guid InstrumentId, DateTimeOffset Time, decimal Long, decimal Short);