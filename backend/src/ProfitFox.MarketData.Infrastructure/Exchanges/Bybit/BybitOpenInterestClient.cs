using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.MarketData.Application.Ports.OpenInterest;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the history of open interest of a linear contract from the public API of Bybit, one request a call. Bybit keeps a point
///     every five minutes, in the base asset, and gives at most a page of them, the newest of a longer range.
/// </summary>
internal sealed class BybitOpenInterestClient(IBybitRestClient bybit, BybitRequestGate gate) : IBybitOpenInterestClient
{
    // The largest page of points Bybit gives.
    private const int PageSize = 200;

    // A page of points five minutes apart.
    public TimeSpan PageLength => TimeSpan.FromMinutes(5 * PageSize);

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
                    OpenInterestInterval.FiveMinutes,
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
                .Where(point => point.Time >= from && point.Time < until)
                .OrderBy(point => point.Time)
        ];
    }
}