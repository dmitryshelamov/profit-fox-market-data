namespace ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

/// <summary>
///     What an exchange tells about an instrument it lists. Every exchange has a listing of its own (<see cref="BybitListedInstrument" />,
///     <see cref="TInvestListedShare" />), and the listings share nothing yet: the base only lets an instrument of the catalog hold the
///     listing of any exchange.
/// </summary>
public abstract record ListedInstrument;