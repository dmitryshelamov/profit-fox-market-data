namespace ProfitFox.MarketData.Application.Ports.OpenInterest;

/// <summary>
///     The open interest of a contract of Bybit at a time of its history, in the base asset.
/// </summary>
public sealed record BybitOpenInterestPoint(DateTimeOffset Time, decimal Value);