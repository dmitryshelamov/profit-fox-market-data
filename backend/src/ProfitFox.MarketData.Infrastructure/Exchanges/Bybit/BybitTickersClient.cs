using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using Microsoft.Extensions.Logging;

using ProfitFox.MarketData.Application.Ports.Tickers;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the tickers of linear from the public API of Bybit, the whole market in one request. Bybit gives the open interest in the
///     base asset and the funding rate as a fraction, empty for a contract that pays no funding. A ticker Bybit tells wrong costs only
///     itself, never the tickers of the rest of the market.
/// </summary>
internal sealed class BybitTickersClient(IBybitRestClient bybit, BybitRequestGate gate, ILogger<BybitTickersClient> logger) : IBybitTickersClient
{
    private const string NoTickers = "Bybit gave no tickers of Linear";

    public async Task<IReadOnlyList<BybitLinearTicker>> GetLinearAsync(CancellationToken cancellationToken)
    {
        BybitResponse<BybitLinearInverseTicker> response = await gate.SendAsync(
            async token => BybitResponses.DataOf(await bybit.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, ct: token), NoTickers),
            cancellationToken);

        var tickers = new Dictionary<string, BybitLinearTicker>();

        foreach (BybitLinearInverseTicker ticker in response.List)
        {
            if (!tickers.TryAdd(ticker.Symbol, new BybitLinearTicker(ticker.Symbol, OpenInterestOf(ticker), ticker.FundingRate)))
            {
                logger.LogWarning("Bybit told the ticker of {Symbol} twice; the first one is kept.", ticker.Symbol);
            }
        }

        return [.. tickers.Values];
    }

    // An open interest below zero cannot be one, so the contract is told to have none.
    private decimal? OpenInterestOf(BybitLinearInverseTicker ticker)
    {
        if (ticker.OpenInterest is not < 0)
        {
            return ticker.OpenInterest;
        }

        logger.LogWarning("Bybit told an open interest of {OpenInterest} for {Symbol}; it is left out.", ticker.OpenInterest, ticker.Symbol);

        return null;
    }
}