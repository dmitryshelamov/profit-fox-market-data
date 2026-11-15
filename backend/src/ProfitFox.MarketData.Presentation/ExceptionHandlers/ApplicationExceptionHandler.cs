using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Presentation.ExceptionHandlers;

/// <summary>
///     Answers the failures the application tells by an exception, the same way for every endpoint: an instrument the catalog does not
///     have is a 404; an exchange that holds the requests or is unavailable is a 503, with <c>Retry-After</c> when the end of the pause
///     is known. Any other exception is left to the default handling.
/// </summary>
internal sealed class ApplicationExceptionHandler(IProblemDetailsService problems, TimeProvider time) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int? status = exception switch
        {
            InstrumentNotFoundException => StatusCodes.Status404NotFound,
            ExchangePausedException or TransientExchangeException => StatusCodes.Status503ServiceUnavailable,
            _ => null
        };

        if (status is null)
        {
            return false;
        }

        if (exception is ExchangePausedException paused)
        {
            double seconds = Math.Ceiling((paused.Until - time.GetUtcNow()).TotalSeconds);
            httpContext.Response.Headers.RetryAfter = Math.Max(val1: 1, (int)seconds).ToString();
        }

        httpContext.Response.StatusCode = status.Value;

        return await problems.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails { Status = status, Detail = exception.Message }
            });
    }
}