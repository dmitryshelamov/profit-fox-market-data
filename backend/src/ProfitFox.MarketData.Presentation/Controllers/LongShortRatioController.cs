using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments/{instrumentId:guid}/long-short-ratio")]
public sealed class LongShortRatioController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The stored points of the long/short ratio of the instrument in <c>[from, to)</c>, oldest first, a point every five minutes:
    ///     the shares of the accounts holding the instrument that were net long and net short, fractions. At most 1000 an answer; a
    ///     window that holds more gives in <c>continueFrom</c> the time to ask the rest from, null otherwise. <c>coveredFrom</c> and
    ///     <c>coveredUntil</c> tell how far the history is loaded: between them every point the exchange has is stored, a time outside
    ///     them is not loaded yet; both are null for an instrument with nothing loaded, as one without a ratio is. The newest points
    ///     are loaded every five minutes, once an hour while the history of the instrument loads. Nothing is loaded from the exchange on a read. An instrument the catalog does not have is a 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<LongShortRatioPointsInternal>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid instrumentId,
        [FromQuery] [BindRequired] DateTimeOffset from,
        [FromQuery] [BindRequired] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        PointWindow window;

        try
        {
            window = new PointWindow(from, to);
        }
        catch (ArgumentException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        LongShortRatioPointsInternal points = await sender.Send(new GetLongShortRatioQuery(instrumentId, window), cancellationToken);

        return Ok(points);
    }
}