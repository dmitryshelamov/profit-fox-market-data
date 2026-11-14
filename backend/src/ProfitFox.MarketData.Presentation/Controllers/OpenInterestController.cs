using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Queries.OpenInterest;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments/{instrumentId:guid}/open-interest")]
public sealed class OpenInterestController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The stored points of open interest of the instrument in <c>[from, to)</c>, oldest first, each with the time it was collected
    ///     for; at most 1000 an answer. A window that holds more gives in <c>continueFrom</c> the time to ask the rest from, null
    ///     otherwise. Nothing is loaded from the exchange: an instrument without open interest, or with none collected in the window, gives
    ///     no points. An instrument the catalog does not have is a 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<OpenInterestPointsInternal>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid instrumentId,
        [FromQuery] [BindRequired] DateTimeOffset from,
        [FromQuery] [BindRequired] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        OpenInterestWindow window;

        try
        {
            window = new OpenInterestWindow(from, to);
        }
        catch (ArgumentException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        OpenInterestPointsInternal points = await sender.Send(new GetOpenInterestQuery(instrumentId, window), cancellationToken);

        return Ok(points);
    }
}