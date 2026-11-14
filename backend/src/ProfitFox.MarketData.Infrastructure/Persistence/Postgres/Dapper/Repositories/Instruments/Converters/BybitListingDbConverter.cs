using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Contracts;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories.Instruments.Converters;

/// <summary>
///     Stores enums by the names of their members, so renaming a member needs a migration of the rows that hold it.
/// </summary>
internal static class BybitListingDbConverter
{
    internal static BybitListingDb FromDomain(Guid instrumentId, BybitListedInstrument listing)
    {
        return new BybitListingDb
        {
            InstrumentId = instrumentId,
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
            LaunchTime = listing.LaunchTime
        };
    }

    internal static BybitListedInstrument ToDomain(BybitListingDb row)
    {
        return new BybitListedInstrument(
            Parse<BybitCategory>(row.InstrumentId, row.Category),
            row.Symbol,
            row.BaseAsset,
            row.QuoteAsset,
            Parse<BybitInstrumentStatus>(row.InstrumentId, row.Status),
            new InstrumentRules(row.TickSize, row.QuantityStep, row.MinQuantity, row.MinAmount),
            row.ContractType is null ? null : Parse<BybitContractType>(row.InstrumentId, row.ContractType),
            row.LaunchTime);
    }

    private static T Parse<T>(Guid instrumentId, string name)
        where T : struct, Enum
    {
        return Enum.TryParse(name, ignoreCase: false, out T value) && Enum.IsDefined(value)
            ? value
            : throw new InvalidOperationException($"Instrument {instrumentId} has an unknown {typeof(T).Name} '{name}'.");
    }
}