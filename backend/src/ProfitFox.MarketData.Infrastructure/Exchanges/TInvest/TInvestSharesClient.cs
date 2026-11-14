using Grpc.Core;

using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Ports.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest.Converters;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Reads the shares of TQBR from the Invest API of T-Invest with the token of the settings, read before every call: one answer of
///     <c>Shares</c> with the base status, the instruments that trade through the API. A failed call fails the whole list, as a list cut
///     short would delist the shares it misses.
/// </summary>
internal sealed class TInvestSharesClient(
    InstrumentsService.InstrumentsServiceClient instruments,
    IOptionsMonitor<TInvestApiSettings> settings,
    TimeProvider time) : ITInvestSharesClient
{
    // One answer holds every share of T-Invest.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <exception cref="NotSupportedException">TInvestApi:Token is not set; no call goes.</exception>
    public async Task<IReadOnlyList<TInvestListedShare>> ListAsync(CancellationToken cancellationToken)
    {
        TInvestApiSettings tinvest = settings.CurrentValue;

        if (!tinvest.HasToken)
        {
            throw new NotSupportedException("The catalog does not sync the shares of T-Invest: TInvestApi:Token is not set.");
        }

        var headers = new Metadata { { "authorization", $"Bearer {tinvest.Token}" }, { "x-app-name", tinvest.AppName } };
        var request = new InstrumentsRequest { InstrumentStatus = InstrumentStatus.Base };

        SharesResponse response = await instruments.SharesAsync(request, headers, time.GetUtcNow().UtcDateTime + Timeout, cancellationToken);

        return [.. response.Instruments.Where(TInvestShareConverter.IsListed).Select(TInvestShareConverter.ToDomain)];
    }
}