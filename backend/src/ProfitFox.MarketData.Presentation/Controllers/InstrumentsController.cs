using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments")]
public sealed class InstrumentsController(ISender sender, TimeProvider time) : ControllerBase
{
    /// <summary>
    ///     Every instrument of the catalog, listed or delisted.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<InstrumentInternal>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<InstrumentInternal> instruments = await sender.Send(new GetInstrumentsQuery(), cancellationToken);

        return Ok(instruments);
    }

    /// <summary>
    ///     Brings the catalog of the category in line with the list of Bybit now; a category the catalog does not sync is a 400.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync([FromQuery] BybitCategory category, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new SyncBybitInstrumentsCommand(category, time.GetUtcNow()), cancellationToken);

            return NoContent();
        }
        catch (NotSupportedException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}