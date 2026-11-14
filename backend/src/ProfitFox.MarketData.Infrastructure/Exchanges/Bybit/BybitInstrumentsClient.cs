using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;

using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit.Converters;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the instruments of spot and linear from the public API of Bybit. Bybit lists only the instruments that trade now.
/// </summary>
internal sealed class BybitInstrumentsClient(IBybitRestClient bybit) : IBybitInstrumentsClient
{
    // The largest page of linear instruments Bybit gives.
    private const int PageSize = 1000;

    public async Task<IReadOnlyList<BybitListedInstrument>> ListAsync(BybitCategory category, CancellationToken cancellationToken)
    {
        if (category == BybitCategory.Spot)
        {
            return await ListSpotAsync(cancellationToken);
        }

        if (category == BybitCategory.Linear)
        {
            return await ListLinearAsync(cancellationToken);
        }

        throw new NotSupportedException($"The catalog does not sync {category} instruments of Bybit.");
    }

    // Bybit gives every spot pair in one answer.
    private async Task<IReadOnlyList<BybitListedInstrument>> ListSpotAsync(CancellationToken cancellationToken)
    {
        HttpResult<BybitResponse<BybitSpotSymbol>> response = await bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(ct: cancellationToken);
        ThrowIfFailed(response, BybitCategory.Spot);

        return response.Data!.List.Select(symbol => symbol.ToDomain()).ToList();
    }

    // Bybit gives linear contracts page by page: each page has the cursor of the next one, the last page has none.
    private async Task<IReadOnlyList<BybitListedInstrument>> ListLinearAsync(CancellationToken cancellationToken)
    {
        var instruments = new List<BybitListedInstrument>();
        string? cursor = null;

        while (true)
        {
            HttpResult<BybitResponse<BybitLinearInverseSymbol>> response = await bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(
                Category.Linear,
                limit: PageSize,
                cursor: cursor,
                ct: cancellationToken);
            ThrowIfFailed(response, BybitCategory.Linear);

            instruments.AddRange(response.Data!.List.Select(symbol => symbol.ToDomain()));

            cursor = response.Data.NextPageCursor;

            if (string.IsNullOrEmpty(cursor))
            {
                return instruments;
            }
        }
    }

    // A failed page fails the whole list: a list cut short would delist the instruments it misses.
    private static void ThrowIfFailed<T>(HttpResult<T> response, BybitCategory category)
    {
        if (!response.Success)
        {
            throw new HttpRequestException($"Bybit did not list {category} instruments: {response.Error}", inner: null, response.ResponseStatusCode);
        }
    }
}