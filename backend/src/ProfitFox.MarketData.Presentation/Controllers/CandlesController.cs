using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Commands.Candles.EnsureCandlesLoaded;
using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Presentation.Settings;

namespace ProfitFox.MarketData.Presentation.Controllers;

[ApiController]
[Route("api/instruments/{instrumentId:guid}/candles")]
public sealed class CandlesController(ISender sender, IOptionsMonitor<CandleReadSettings> settings, TimeProvider time) : ControllerBase
{
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
    ///     1000; <paramref name="from" /> and <paramref name="to" /> start candles of the interval. Minutes storage does not hold are
    ///     loaded from the exchange first; a 202 says the window is still loading: ask again later. An instrument the catalog does not
    ///     have is a 404.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<CandlesInternal>(StatusCodes.Status200OK)]
    [ProducesResponseType<CandlesInternal>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid instrumentId,
        [FromQuery, BindRequired] string interval,
        [FromQuery, BindRequired] DateTimeOffset from,
        [FromQuery, BindRequired] DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (!Intervals.TryGetValue(interval, out CandleInterval candleInterval))
        {
            return Problem($"The interval must be one of {string.Join(", ", Intervals.Keys)}.", statusCode: StatusCodes.Status400BadRequest);
        }

        CandleWindow window;

        try
        {
            window = new CandleWindow(candleInterval, from, to);
        }
        catch (ArgumentException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        CandlesReadiness readiness = await sender.Send(
            new EnsureCandlesLoadedCommand(instrumentId, window.Range, time.GetUtcNow(), settings.CurrentValue.MaxExchangeRequests),
            cancellationToken);
        CandlesInternal candles = await sender.Send(new GetCandlesQuery(instrumentId, window), cancellationToken);

        // A window still loading gives no candles yet, only what of it storage holds by now.
        return readiness == CandlesReadiness.Loading
            ? StatusCode(StatusCodes.Status202Accepted, candles with { Loading = true, Candles = [] })
            : Ok(candles);
    }
}