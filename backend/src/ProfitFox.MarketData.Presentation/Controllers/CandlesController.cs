using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments/{instrumentId:guid}/candles")]
public sealed class CandlesController(ISender sender, TimeProvider time) : ControllerBase
{
    private const int MaxCandles = 1000;

    private static readonly Dictionary<string, CandleInterval> Intervals = new Dictionary<string, CandleInterval>
    {
        ["1m"] = CandleInterval.OneMinute,
        ["5m"] = CandleInterval.FiveMinutes,
        ["15m"] = CandleInterval.FifteenMinutes,
        ["1h"] = CandleInterval.OneHour,
        ["4h"] = CandleInterval.FourHours,
        ["1d"] = CandleInterval.OneDay
    };

    /// <summary>
    ///     The candles of <paramref name="interval" /> (1m, 5m, 15m, 1h, 4h or 1d, in UTC) of the instrument in <c>[from, to)</c>, at most
    ///     1000; <paramref name="from" /> and <paramref name="to" /> start candles of the interval. A 202 says the window is still loading
    ///     from the exchange: ask again later.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<CandlesInternal>(StatusCodes.Status200OK)]
    [ProducesResponseType<CandlesInternal>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid instrumentId,
        [FromQuery][BindRequired] string interval,
        [FromQuery][BindRequired] DateTimeOffset from,
        [FromQuery][BindRequired] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (!Intervals.TryGetValue(interval, out CandleInterval candleInterval))
        {
            return Problem($"The interval must be one of {string.Join(", ", Intervals.Keys)}.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (to <= from)
        {
            return Problem("The window must end after it starts.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!candleInterval.StartsAt(from) || !candleInterval.StartsAt(to))
        {
            return Problem($"The window must start and end where candles of {interval} start, in UTC.", statusCode: StatusCodes.Status400BadRequest);
        }

        if ((to - from) / candleInterval.Length() > MaxCandles)
        {
            return Problem($"A window holds at most {MaxCandles} candles.", statusCode: StatusCodes.Status400BadRequest);
        }

        CandlesInternal? candles = await sender.Send(new GetCandlesQuery(instrumentId, candleInterval, from, to, time.GetUtcNow()), cancellationToken);

        if (candles is null)
        {
            return Problem($"The catalog has no instrument {instrumentId}.", statusCode: StatusCodes.Status404NotFound);
        }

        return candles.Loading ? StatusCode(StatusCodes.Status202Accepted, candles) : Ok(candles);
    }
}