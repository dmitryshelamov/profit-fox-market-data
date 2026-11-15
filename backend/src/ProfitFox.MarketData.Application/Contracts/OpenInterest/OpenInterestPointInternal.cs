namespace ProfitFox.MarketData.Application.Contracts.OpenInterest;

/// <summary>
///     The open interest of an instrument at a time, in the base asset, as it is stored.
/// </summary>
public sealed record OpenInterestPointInternal(DateTimeOffset Time, decimal OpenInterest);