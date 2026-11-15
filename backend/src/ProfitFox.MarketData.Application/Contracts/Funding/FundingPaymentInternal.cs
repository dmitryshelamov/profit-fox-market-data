namespace ProfitFox.MarketData.Application.Contracts.Funding;

/// <summary>
///     A funding payment of an instrument: the rate its exchange settled at a time, a fraction, as it is stored.
/// </summary>
public sealed record FundingPaymentInternal(DateTimeOffset Time, decimal FundingRate);