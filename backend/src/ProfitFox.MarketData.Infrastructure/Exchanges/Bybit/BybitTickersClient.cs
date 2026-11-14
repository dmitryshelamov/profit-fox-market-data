using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.MarketData.Application.Ports.Tickers;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the tickers of linear from the public API of Bybit, the whole market in one request. Bybit gives the open interest in the
///     base asset and the funding rate as a fraction, empty for a contract that pays no funding.
/// </summary>
internal sealed class BybitTickersClient(IBybitRestClient bybit, BybitRequestGate gate) : IBybitTickersClient
{
    private const string NoTickers = "Bybit gave no tickers of Linear";

    public async Task<IReadOnlyList<BybitLinearTicker>> GetLinearAsync(CancellationToken cancellationToken)
    {
        BybitResponse<BybitLinearInverseTicker> response = await gate.SendAsync(
            async token => BybitResponses.DataOf(await bybit.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, ct: token), NoTickers),
            cancellationToken);

        return [.. response.List.Select(ticker => new BybitLinearTicker(ticker.Symbol, ticker.OpenInterest, ticker.FundingRate))];
    }
}