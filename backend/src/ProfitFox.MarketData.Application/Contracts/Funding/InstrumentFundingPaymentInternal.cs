namespace ProfitFox.MarketData.Application.Contracts.Funding;

/// <summary>
///     A funding payment of an instrument: the rate its exchange settled at a time, a fraction, as it is stored: what the feed of
///     funding payments tells, of the whole market, so it names its instrument.
/// </summary>
public sealed record InstrumentFundingPaymentInternal(Guid InstrumentId, DateTimeOffset Time, decimal FundingRate);