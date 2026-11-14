using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit.Converters;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the instruments of spot and linear from the public API of Bybit. Bybit lists only the instruments that trade now.
/// </summary>
internal sealed class BybitInstrumentsClient(IBybitRestClient bybit, BybitRequestGate gate) : IBybitInstrumentsClient
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
        BybitResponse<BybitSpotSymbol> response = await gate.SendAsync(
            async token => BybitResponses.DataOf(await bybit.V5Api.ExchangeData.GetSpotSymbolsAsync(ct: token), NotListed(BybitCategory.Spot)),
            cancellationToken);

        return response.List.Select(symbol => symbol.ToDomain()).ToList();
    }

    // Bybit gives linear contracts page by page: each page has the cursor of the next one, the last page has none.
    private async Task<IReadOnlyList<BybitListedInstrument>> ListLinearAsync(CancellationToken cancellationToken)
    {
        var instruments = new List<BybitListedInstrument>();
        string? cursor = null;

        while (true)
        {
            string? pageCursor = cursor;
            BybitResponse<BybitLinearInverseSymbol> response = await gate.SendAsync(
                async token => BybitResponses.DataOf(
                    await bybit.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, limit: PageSize, cursor: pageCursor, ct: token),
                    NotListed(BybitCategory.Linear)),
                cancellationToken);

            instruments.AddRange(response.List.Select(symbol => symbol.ToDomain()));

            cursor = response.NextPageCursor;

            if (string.IsNullOrEmpty(cursor))
            {
                return instruments;
            }
        }
    }

    // A failed page fails the whole list: a list cut short would delist the instruments it misses.
    private static string NotListed(BybitCategory category)
    {
        return $"Bybit did not list {category} instruments";
    }
}