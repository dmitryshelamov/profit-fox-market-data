using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

/// <summary>
///     What an exchange tells about an instrument it lists. Every exchange has a listing of its own (<see cref="BybitListedInstrument" />,
///     <see cref="TInvestListedShare" />); all they share is the exchange they are of, by which the instruments of one exchange are read
///     and the client of that exchange is picked.
/// </summary>
public abstract record ListedInstrument
{
    public abstract Exchange Exchange { get; }
}