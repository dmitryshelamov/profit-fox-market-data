namespace ProfitFox.MarketData.Application.Contracts.OpenInterest;

/// <summary>
///     The open interest of an instrument at a time, in the base asset, as it is stored: what the feed of open interest tells, of
///     the whole market, so it names its instrument.
/// </summary>
public sealed record InstrumentOpenInterestInternal(Guid InstrumentId, DateTimeOffset Time, decimal OpenInterest);