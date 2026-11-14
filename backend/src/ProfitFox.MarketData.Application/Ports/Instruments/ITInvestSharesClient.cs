using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.Instruments;

/// <summary>
///     The shares of TQBR that T-Invest lists now.
/// </summary>
public interface ITInvestSharesClient
{
    /// <summary>
    ///     Every share of TQBR in roubles that trades through the API of T-Invest, the whole list or none: a list cut short would delist the rest.
    /// </summary>
    Task<IReadOnlyList<TInvestListedShare>> ListAsync(CancellationToken cancellationToken);
}