using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Contracts.Instruments;

public sealed record BybitListedInstrumentInternal(
    BybitCategory Category,
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    InstrumentStatus Status,
    InstrumentRulesInternal Rules,
    BybitContractType? ContractType,
    DateTimeOffset? LaunchTime);