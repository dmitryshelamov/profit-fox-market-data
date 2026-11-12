namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

/// <summary>
///     A market of Bybit:
///     <see cref="Spot" /> pairs,
///     <see cref="Linear" /> contracts settled in USDT or USDC,
///     <see cref="Inverse" /> contracts settled in the coin,
///     <see cref="Option" />s. A symbol is unique only within a category:
///     BTCUSDT is listed on both spot and linear.
/// </summary>
public enum BybitCategory
{
    Spot,
    Linear,
    Inverse,
    Option
}