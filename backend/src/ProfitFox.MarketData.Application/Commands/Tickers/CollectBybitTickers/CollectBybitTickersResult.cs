namespace ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;

/// <summary>
///     How many points of each series a snapshot saved; a dated futures contract has no funding rate, so the rates are fewer.
/// </summary>
public sealed record CollectBybitTickersResult(int OpenInterestPoints, int FundingRatePoints);