using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries.Funding;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments/{instrumentId:guid}/funding-rate")]
public sealed class FundingRateController(ISender sender) : ControllerBase
{
    /// <summary>
    ///     The stored points of the funding rate of the coming payment of the instrument in <c>[from, to)</c>, oldest first, each with
    ///     the time it was collected for; at most 1000 an answer. A window that holds more gives in <c>continueFrom</c> the time to ask
    ///     the rest from, null otherwise. Nothing is loaded from the exchange: an instrument without funding, or with none collected in
    ///     the window, gives no points. An instrument the catalog does not have is a 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<FundingRatePointsInternal>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid instrumentId,
        [FromQuery] [BindRequired] DateTimeOffset from,
        [FromQuery] [BindRequired] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        FundingRateWindow window;

        try
        {
            window = new FundingRateWindow(from, to);
        }
        catch (ArgumentException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        FundingRatePointsInternal points = await sender.Send(new GetFundingRateQuery(instrumentId, window), cancellationToken);

        return Ok(points);
    }
}