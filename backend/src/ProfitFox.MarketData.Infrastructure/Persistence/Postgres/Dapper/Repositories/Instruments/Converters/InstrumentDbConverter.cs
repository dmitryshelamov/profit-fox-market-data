using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Converters;

/// <summary>
///     Stores enums by the names of their members, so renaming a member needs a migration of the rows that hold it.
/// </summary>
internal static class InstrumentDbConverter
{
    internal static InstrumentDb FromDomain(Instrument instrument)
    {
        BybitListedInstrument listing = instrument.Listing as BybitListedInstrument
            ?? throw new NotSupportedException($"Instrument {instrument.Id} has a listing of {instrument.Listing.GetType().Name}, which the catalog does not store yet.");

        return new InstrumentDb
        {
            Id = instrument.Id,
            Category = listing.Category.ToString(),
            Symbol = listing.Symbol,
            BaseAsset = listing.BaseAsset,
            QuoteAsset = listing.QuoteAsset,
            Status = listing.Status.ToString(),
            TickSize = listing.Rules.TickSize,
            QuantityStep = listing.Rules.QuantityStep,
            MinQuantity = listing.Rules.MinQuantity,
            MinAmount = listing.Rules.MinAmount,
            ContractType = listing.ContractType?.ToString(),
            LaunchTime = listing.LaunchTime,
            FirstSeenAt = instrument.FirstSeenAt,
            DelistedAt = instrument.DelistedAt
        };
    }

    internal static Instrument ToDomain(InstrumentDb row)
    {
        var listing = new BybitListedInstrument(
            Parse<BybitCategory>(row.Id, row.Category),
            row.Symbol,
            row.BaseAsset,
            row.QuoteAsset,
            Parse<InstrumentStatus>(row.Id, row.Status),
            new InstrumentRules(row.TickSize, row.QuantityStep, row.MinQuantity, row.MinAmount),
            row.ContractType is null ? null : Parse<BybitContractType>(row.Id, row.ContractType),
            row.LaunchTime);

        return Instrument.Restore(row.Id, listing, row.FirstSeenAt, row.DelistedAt);
    }

    private static T Parse<T>(Guid id, string name)
        where T : struct, Enum
    {
        return Enum.TryParse(name, ignoreCase: false, out T value) && Enum.IsDefined(value)
            ? value
            : throw new InvalidOperationException($"Instrument {id} has an unknown {typeof(T).Name} '{name}'.");
    }
}