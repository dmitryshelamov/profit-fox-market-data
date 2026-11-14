namespace ProfitFox.MarketData.Application.Ports.Tickers;

/// <summary>
///     What the ticker of a linear contract of Bybit tells of it at the moment it is asked: its open interest in the base asset and the
///     funding rate of its coming payment, a fraction. Either is null when Bybit leaves it empty: a dated futures contract pays no
///     funding and has no rate.
/// </summary>
public sealed record BybitLinearTicker(string Symbol, decimal? OpenInterest, decimal? FundingRate);