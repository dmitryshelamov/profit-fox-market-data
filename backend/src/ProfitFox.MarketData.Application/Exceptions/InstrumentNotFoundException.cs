namespace ProfitFox.MarketData.Application.Exceptions;

/// <summary>
///     The catalog never kept an instrument of the id.
/// </summary>
public sealed class InstrumentNotFoundException(Guid instrumentId) : Exception($"The catalog has no instrument {instrumentId}.");