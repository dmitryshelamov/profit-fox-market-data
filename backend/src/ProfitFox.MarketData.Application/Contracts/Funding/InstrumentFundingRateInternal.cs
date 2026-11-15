namespace ProfitFox.MarketData.Application.Contracts.Funding;

/// <summary>
///     The funding rate of the coming payment of an instrument as it was at a time, a fraction, as it is stored: what the feed of
///     funding rates tells, of the whole market, so it names its instrument.
/// </summary>
public sealed record InstrumentFundingRateInternal(Guid InstrumentId, DateTimeOffset Time, decimal FundingRate);