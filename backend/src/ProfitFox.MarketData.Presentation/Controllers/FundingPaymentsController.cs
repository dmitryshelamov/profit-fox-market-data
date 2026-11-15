using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Funding;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments/{instrumentId:guid}/funding-payments")]
public sealed class FundingPaymentsController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The stored funding payments of the instrument in <c>[from, to)</c>, oldest first, each with the time it was made at and its
    ///     rate; at most 1000 an answer. A window that holds more gives in <c>continueFrom</c> the time to ask the rest from, null
    ///     otherwise. <c>coveredFrom</c> and <c>coveredUntil</c> tell how far the payments are loaded: a time without a payment between
    ///     them had none, a time outside them is not loaded yet; both are null for an instrument with nothing loaded, as one that pays no
    ///     funding is. Nothing is loaded from the exchange. An instrument the catalog does not have is a 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<FundingPaymentsInternal>(StatusCodes.Status200OK)]
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

        FundingPaymentsInternal payments = await sender.Send(new GetFundingPaymentsQuery(instrumentId, window), cancellationToken);

        return Ok(payments);
    }
}