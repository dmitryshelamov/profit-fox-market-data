using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using Microsoft.Extensions.Logging;

using ProfitFox.MarketData.Application.Ports.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the history of the long/short ratio of a linear contract from the public API of Bybit, one request a call. Bybit keeps a
///     point every five minutes, the shares of the accounts net long and net short, and gives at most a page of them, the newest of a
///     longer range. A point Bybit tells wrong costs only itself, never the page: the loading of the contract would stop at that page
///     for good.
/// </summary>
internal sealed class BybitLongShortRatioClient(IBybitRestClient bybit, BybitRequestGate gate, ILogger<BybitLongShortRatioClient> logger)
    : IBybitLongShortRatioClient
{
    // The largest page of points Bybit gives.
    private const int PageSize = 500;

    // The step of the history asked for: the one of the series.
    private const DataPeriod Step = DataPeriod.FiveMinutes;

    public TimeSpan PageLength => LongShortRatioSeries.Step * PageSize;

    public async Task<IReadOnlyList<BybitLongShortRatioPoint>> GetAsync(
        BybitListedInstrument listing,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        // Bybit takes both ends as included: the last millisecond before the end keeps the point of the end out.
        BybitLongShortRatio[] told = await gate.SendAsync(
            async token => BybitResponses.DataOf(
                await bybit.V5Api.ExchangeData.GetLongShortRatioAsync(
                    Category.Linear,
                    listing.Symbol,
                    Step,
                    from.UtcDateTime,
                    until.UtcDateTime.AddMilliseconds(-1),
                    PageSize,
                    token),
                $"Bybit gave no long/short ratio of {listing.Symbol}"),
            cancellationToken);

        // Bybit gives the newest first; a point outside the range is left out.
        return
        [
            .. told
                .Select(point => new BybitLongShortRatioPoint(
                    new DateTimeOffset(DateTime.SpecifyKind(point.Timestamp, DateTimeKind.Utc)),
                    point.BuyRatio,
                    point.SellRatio))
                .Where(point => point.Time >= from && point.Time < until && IsPoint(point, listing))
                .OrderBy(point => point.Time)
        ];
    }

    // A share below zero cannot be one, and a point between two steps is of no step of the series.
    private bool IsPoint(BybitLongShortRatioPoint point, BybitListedInstrument listing)
    {
        if (point.Long >= 0 && point.Short >= 0 && LongShortRatioSeries.StartsStep(point.Time))
        {
            return true;
        }

        logger.LogWarning(
            "Bybit told a long/short ratio of {Long} to {Short} at {Time:O} in the history of {Symbol}; it is left out.",
            point.Long,
            point.Short,
            point.Time,
            listing.Symbol);

        return false;
    }
}