namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>
///     Storage did not answer a statement within the time it was given: a failure that may pass by itself, so the caller reads again.
/// </summary>
public sealed class StorageTimeoutException(int seconds, Exception? innerException = null)
    : Exception($"The storage did not answer within {seconds} s.", innerException);