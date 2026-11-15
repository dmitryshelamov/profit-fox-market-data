using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;

/// <summary>
///     A listed instrument of the catalog: a linear BTCUSDT unless the test gives its listing, a new id unless it gives one.
/// </summary>
public static class InstrumentGenerator
{
    private static readonly DateTimeOffset FirstSeenAt = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    public static Instrument Generate(ListedInstrument? listing = null, DateTimeOffset? firstSeenAt = null, Guid? id = null)
    {
        return Instrument.Create(
            id ?? Guid.CreateVersion7(),
            listing ?? BybitListedInstrumentGenerator.GenerateLinear(),
            firstSeenAt ?? FirstSeenAt);
    }
}