namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

/// <summary>
///     The kind of a contract of Bybit on linear or inverse: perpetual (no expiration) or futures (with a delivery time).
/// </summary>
public enum BybitContractType
{
    LinearPerpetual,
    LinearFutures,
    InversePerpetual,
    InverseFutures
}