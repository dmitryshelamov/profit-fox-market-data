using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.Instruments;

/// <summary>
///     The instruments Bybit lists now.
/// </summary>
public interface IBybitInstrumentsClient
{
    /// <summary>
    ///     Every instrument Bybit lists in <paramref name="category" />, the whole list or none: a list cut short would delist the rest.
    /// </summary>
    Task<IReadOnlyList<BybitListedInstrument>> ListAsync(BybitCategory category, CancellationToken cancellationToken);
}