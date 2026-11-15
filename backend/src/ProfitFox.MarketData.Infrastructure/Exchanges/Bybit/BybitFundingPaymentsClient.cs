using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.MarketData.Application.Ports.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the funding payments of a linear contract from the public API of Bybit, one request a call. Bybit gives the rate as a
///     fraction and, of a range that holds more payments than a page, the newest ones.
/// </summary>
internal sealed class BybitFundingPaymentsClient(IBybitRestClient bybit, BybitRequestGate gate) : IBybitFundingPaymentsClient
{
    // The largest page of funding payments Bybit gives.
    public int PageSize => 200;

    public async Task<IReadOnlyList<FundingPayment>> GetAsync(
        BybitListedInstrument listing,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        // Bybit takes both ends as included: the last millisecond before the end keeps a payment made at the end out.
        BybitResponse<BybitFundingHistory> response = await gate.SendAsync(
            async token => BybitResponses.DataOf(
                await bybit.V5Api.ExchangeData.GetFundingRateHistoryAsync(
                    Category.Linear,
                    listing.Symbol,
                    from.UtcDateTime,
                    until.UtcDateTime.AddMilliseconds(-1),
                    PageSize,
                    token),
                $"Bybit gave no funding payments of {listing.Symbol}"),
            cancellationToken);

        // Bybit gives the newest first; a payment outside the range is left out.
        return
        [
            .. response.List
                .Select(payment => new FundingPayment(new DateTimeOffset(DateTime.SpecifyKind(payment.Timestamp, DateTimeKind.Utc)), payment.FundingRate))
                .Where(payment => payment.Time >= from && payment.Time < until)
                .OrderBy(payment => payment.Time)
        ];
    }
}