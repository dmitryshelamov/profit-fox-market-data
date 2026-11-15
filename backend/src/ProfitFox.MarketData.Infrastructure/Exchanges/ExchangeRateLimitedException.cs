namespace ProfitFox.MarketData.Infrastructure.Exchanges;

/// <summary>
///     The exchange said the requests went over its limit, or banned the IP for it (<see cref="IpBanned" />). It is never retried: another
///     attempt would hit the limit again.
/// </summary>
internal sealed class ExchangeRateLimitedException(string message, bool ipBanned) : Exception(message)
{
    public bool IpBanned { get; } = ipBanned;
}