namespace ProfitFox.MarketData.Infrastructure.Exchanges;

/// <summary>
///     A failure of a request to an exchange that may pass by itself: a server error, a timeout, a lost connection. It is retried.
/// </summary>
internal sealed class TransientExchangeException(string message, Exception? innerException = null) : Exception(message, innerException);