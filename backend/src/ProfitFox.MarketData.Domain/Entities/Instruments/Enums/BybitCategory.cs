namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

/// <summary>
///     A market of Bybit the catalog keeps: <see cref="Spot" /> pairs and <see cref="Linear" /> contracts settled in USDT or USDC. A symbol
///     is unique only within a category: BTCUSDT is listed on both.
/// </summary>
public enum BybitCategory
{
    Spot,
    Linear
}