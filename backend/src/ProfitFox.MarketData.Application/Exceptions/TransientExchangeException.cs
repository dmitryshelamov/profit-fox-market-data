namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>
///     A failure of a request to an exchange that may pass by itself: a server error, a timeout, a lost connection. The client of the
///     exchange retries it; one that reaches a caller means the exchange is unavailable for now.
/// </summary>
public sealed class TransientExchangeException(string message, Exception? innerException = null) : Exception(message, innerException);