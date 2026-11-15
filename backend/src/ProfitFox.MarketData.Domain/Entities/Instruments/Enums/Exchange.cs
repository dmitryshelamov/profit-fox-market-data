namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

/// <summary>
///     Where the catalog takes its instruments from: <see cref="Bybit" />, or the shares of MOEX through <see cref="TInvest" />.
/// </summary>
public enum Exchange
{
    Bybit,
    TInvest
}