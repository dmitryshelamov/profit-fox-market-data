namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

/// <summary>
///     The kind of a linear contract of Bybit: perpetual (no expiration) or futures (with a delivery time).
/// </summary>
public enum BybitContractType
{
    LinearPerpetual,
    LinearFutures
}