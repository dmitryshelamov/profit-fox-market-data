namespace ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

/// <summary>
///     Whether an instrument trades: <see cref="PreLaunch" /> a pre-market contract, <see cref="PendingOpen" /> listed but not open yet,
///     <see cref="Trading" />, <see cref="Delivering" /> a contract being settled at its expiration, <see cref="Closed" /> no longer traded.
/// </summary>
public enum InstrumentStatus
{
    PreLaunch,
    PendingOpen,
    Trading,
    Delivering,
    Closed
}