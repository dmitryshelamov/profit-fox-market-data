namespace ProfitFox.MarketData.Infrastructure.Exchanges;

/// <summary>
///     No request goes to the exchange until <see cref="Until" />, after it limited them; the request failed without waiting, so the caller
///     waits outside its transaction.
/// </summary>
internal sealed class ExchangePausedException(string message, DateTimeOffset until, Exception? innerException = null)
    : Exception(message, innerException)
{
    public DateTimeOffset Until { get; } = until;
}