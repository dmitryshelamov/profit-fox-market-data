using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Converters;

internal static class InstrumentDbConverter
{
    internal static InstrumentDb FromDomain(Instrument instrument)
    {
        return new InstrumentDb
        {
            Id = instrument.Id,
            FirstSeenAt = instrument.FirstSeenAt,
            DelistedAt = instrument.DelistedAt
        };
    }

    internal static Instrument ToDomain(InstrumentDb row, ListedInstrument listing)
    {
        return Instrument.Restore(row.Id, listing, row.FirstSeenAt, row.DelistedAt);
    }
}