using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Instruments;

[TestFixture]
public class TInvestListedShareTests
{
    private static readonly InstrumentRules Rules = new InstrumentRules(tickSize: 0.01m, quantityStep: 10m, minQuantity: 10m);

    [TestCase("", "SBER")]
    [TestCase(" ", "SBER")]
    [TestCase("e6123145-9665-43e0-8413-cd61b8aa9b13", "")]
    [TestCase("e6123145-9665-43e0-8413-cd61b8aa9b13", " ")]
    public void Blank_uid_or_ticker_is_rejected(string uid, string ticker)
    {
        Assert.Throws<ArgumentException>(() => new TInvestListedShare(uid, ticker, Rules));
    }
}