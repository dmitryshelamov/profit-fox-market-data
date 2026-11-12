using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.Instruments;

/// <summary>
///     The instruments the exchange lists now.
/// </summary>
public interface IExchangeInstruments
{
    /// <summary>
    ///     Every instrument the exchange lists in <paramref name="category" />, the whole list or none: a list cut short would delist the rest.
    /// </summary>
    Task<IReadOnlyList<BybitListedInstrument>> ListAsync(BybitCategory category, CancellationToken cancellationToken);
}