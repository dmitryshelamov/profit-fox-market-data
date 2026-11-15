using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

/// <summary>
///     Shares of TQBR as T-Invest lists them: SBER with a lot of 10 and a price step of a kopeck, and a new uid unless the test gives one.
/// </summary>
public static class TInvestListedShareGenerator
{
    public static TInvestListedShare Generate(string ticker = "SBER", decimal tickSize = 0.01m, string? uid = null)
    {
        return new TInvestListedShare(
            uid ?? Guid.NewGuid().ToString(),
            ticker,
            new InstrumentRules(tickSize, quantityStep: 10m, minQuantity: 10m));
    }
}