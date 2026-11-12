using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitFox.MarketData.Application.Commands.Instruments.Base;
using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;
using ProfitFox.MarketData.Application.Commands.Instruments.SyncTInvestShares;
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
    [HttpPost("bybit/sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> SyncBybit([FromQuery] BybitCategory category, CancellationToken cancellationToken)
    {
        return SyncAsync(new SyncBybitInstrumentsCommand(category, time.GetUtcNow()), cancellationToken);
    }

    /// <summary>
    ///     Brings the shares of the catalog in line with the list of T-Invest now; without a token of T-Invest it is a 400.
    /// </summary>
    [HttpPost("tinvest/sync")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> SyncTInvest(CancellationToken cancellationToken)
    {
        return SyncAsync(new SyncTInvestSharesCommand(time.GetUtcNow()), cancellationToken);
    }

    // A sync the catalog cannot run (a category it does not sync, an exchange without its token) is refused before any request goes.
    private async Task<IActionResult> SyncAsync(BaseSyncInstrumentsCommand command, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(command, cancellationToken);

            return NoContent();
        }
        catch (NotSupportedException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}