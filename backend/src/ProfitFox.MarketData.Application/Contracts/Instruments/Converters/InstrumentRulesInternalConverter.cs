using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Contracts.Instruments.Converters;

public static class InstrumentRulesInternalConverter
{
    public static InstrumentRulesInternal ToInternal(this InstrumentRules rules)
    {
        return new InstrumentRulesInternal(rules.TickSize, rules.QuantityStep, rules.MinQuantity, rules.MinAmount);
    }
}