using ProfitFox.MarketData.Application.Contracts.Instruments.Base;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Contracts.Instruments;

public sealed record BybitListedInstrumentInternal(
    BybitCategory Category,
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    BybitInstrumentStatus Status,
    InstrumentRulesInternal Rules,
    BybitContractType? ContractType,
    DateTimeOffset? LaunchTime) : ListedInstrumentInternal;