namespace ProfitFox.MarketData.Application.Contracts;

/// <summary>
///     The parts of a window of history that will not change for an instrument, oldest first and apart from one another, cut to the
///     window; none when nothing of it is covered. A read of history gives the rows inside them and no other.
/// </summary>
public sealed record InstrumentCoverageInternal(Guid InstrumentId, IReadOnlyList<TimeRangeInternal> Ranges);