namespace ProfitFox.MarketData.Application.Contracts;

/// <summary>
///     A part of time <c>[From, Until)</c>, of any kind of data: the ends of a range of points need not be on the start of a minute.
/// </summary>
public sealed record TimeRangeInternal(DateTimeOffset From, DateTimeOffset Until);