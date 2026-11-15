using ProfitFox.MarketData.Application.Contracts.Instruments.Base;

namespace ProfitFox.MarketData.Application.Contracts.Instruments;

public sealed record TInvestListedShareInternal(
    string Uid,
    string Ticker,
    InstrumentRulesInternal Rules) : ListedInstrumentInternal;