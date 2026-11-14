using System.Net;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

internal static class BybitResponses
{
    // The retCode of Bybit for requests over its limit.
    private const int TooManyVisits = 10006;

    /// <summary>
    ///     The data of a successful answer. A failure is told by its kind: <see cref="ExchangeRateLimitedException" /> for the limit (HTTP
    ///     429, retCode 10006) or a ban of the IP (HTTP 403), <see cref="TransientExchangeException" /> for a server error, a timeout or a
    ///     lost connection, and <see cref="HttpRequestException" /> for any other; <paramref name="what" /> names what was not done.
    /// </summary>
    internal static T DataOf<T>(HttpResult<T> response, string what)
    {
        if (response.Success)
        {
            return response.Data!;
        }

        var message = $"{what}: {response.Error}";

        if (response.ResponseStatusCode is HttpStatusCode.Forbidden)
        {
            throw new ExchangeRateLimitedException(message, ipBanned: true);
        }

        if (response.ResponseStatusCode is HttpStatusCode.TooManyRequests || response.Error?.ErrorType is ErrorType.RateLimitRequest ||
            response.Error?.Code is TooManyVisits)
        {
            throw new ExchangeRateLimitedException(message, ipBanned: false);
        }

        if (response.ResponseStatusCode >= HttpStatusCode.InternalServerError
            || response.Error?.ErrorType is ErrorType.Timeout or ErrorType.NetworkError or ErrorType.UnableToConnect)
        {
            throw new TransientExchangeException(message);
        }

        throw new HttpRequestException(message, inner: null, response.ResponseStatusCode);
    }
}