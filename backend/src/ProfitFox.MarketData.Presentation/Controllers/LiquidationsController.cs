using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Liquidations;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments/{instrumentId:guid}/liquidations")]
public sealed class LiquidationsController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The stored minutes of liquidations of the instrument in <c>[from, to)</c>, oldest first, each with the sums of the
    ///     liquidated longs and shorts; at most 1000 an answer. A window that holds more gives in <c>continueFrom</c> the time to ask
    ///     the rest from, null otherwise. A minute without liquidations is not stored and is not told. A minute with
    ///     <c>isValid</c> false was not heard in full: its sums tell only what was heard, down to nothing. The minute that goes on is
    ///     told with what is stored of it so far. Nothing is loaded from the exchange, which keeps no history of liquidations: an
    ///     instrument without liquidations, or with none heard in the window, gives no minutes. An instrument the catalog does not have
    ///     is a 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<LiquidationMinutesInternal>(StatusCodes.Status200OK)]
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

        LiquidationMinutesInternal minutes = await sender.Send(new GetLiquidationsQuery(instrumentId, window), cancellationToken);

        return Ok(minutes);
    }
}