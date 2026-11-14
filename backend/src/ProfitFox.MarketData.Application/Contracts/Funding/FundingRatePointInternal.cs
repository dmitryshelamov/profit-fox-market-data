namespace ProfitFox.MarketData.Application.Contracts.Funding;

/// <summary>
///     The funding rate of the coming payment of an instrument as it was at a time, a fraction, as it is stored.
/// </summary>
public sealed record FundingRatePointInternal(DateTimeOffset Time, decimal FundingRate);