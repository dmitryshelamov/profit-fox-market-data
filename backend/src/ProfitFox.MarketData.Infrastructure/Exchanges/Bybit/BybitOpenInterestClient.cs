using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using Microsoft.Extensions.Logging;

using ProfitFox.MarketData.Application.Ports.OpenInterest;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the history of open interest of a linear contract from the public API of Bybit, one request a call. Bybit keeps a point
///     every five minutes, in the base asset, and gives at most a page of them, the newest of a longer range. A point Bybit tells wrong
///     costs only itself, never the page: the loading of the contract would stop at that page for good.
/// </summary>
internal sealed class BybitOpenInterestClient(IBybitRestClient bybit, BybitRequestGate gate, ILogger<BybitOpenInterestClient> logger) : IBybitOpenInterestClient
{
    // The largest page of points Bybit gives.
    private const int PageSize = 200;

    // The step of the history asked for: the one of the series.
    private const OpenInterestInterval Step = OpenInterestInterval.FiveMinutes;

    public TimeSpan PageLength => OpenInterestSeries.Step * PageSize;

    public async Task<IReadOnlyList<BybitOpenInterestPoint>> GetAsync(
        BybitListedInstrument listing,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        // Bybit takes both ends as included: the last millisecond before the end keeps the point of the end out.
        BybitResponse<BybitOpenInterest> response = await gate.SendAsync(
            async token => BybitResponses.DataOf(
                await bybit.V5Api.ExchangeData.GetOpenInterestAsync(
                    Category.Linear,
                    listing.Symbol,
                    Step,
                    from.UtcDateTime,
                    until.UtcDateTime.AddMilliseconds(-1),
                    PageSize,
                    ct: token),
                $"Bybit gave no open interest of {listing.Symbol}"),
            cancellationToken);

        // Bybit gives the newest first, and counts a start between two points from the earlier one: a point outside the range is left out.
        return
        [
            .. response.List
                .Select(point => new BybitOpenInterestPoint(new DateTimeOffset(DateTime.SpecifyKind(point.Timestamp, DateTimeKind.Utc)), point.OpenInterest))
                .Where(point => point.Time >= from && point.Time < until && IsPoint(point, listing))
                .OrderBy(point => point.Time)
        ];
    }

    // An open interest below zero cannot be one, and a point between two steps is of no step of the series.
    private bool IsPoint(BybitOpenInterestPoint point, BybitListedInstrument listing)
    {
        if (point.Value >= 0 && OpenInterestSeries.StartsStep(point.Time))
        {
            return true;
        }

        logger.LogWarning(
            "Bybit told an open interest of {OpenInterest} at {Time:O} in the history of {Symbol}; it is left out.",
            point.Value,
            point.Time,
            listing.Symbol);

        return false;
    }
}