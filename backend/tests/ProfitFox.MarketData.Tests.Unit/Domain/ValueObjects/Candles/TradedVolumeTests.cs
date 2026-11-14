using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Candles;

[TestFixture]
public class TradedVolumeTests
{
    [Test]
    public void A_minute_no_trade_was_seen_in_has_nothing_traded_and_nothing_bought()
    {
        Assert.That(TradedVolume.None, Is.EqualTo(new TradedVolume(traded: 0m, bought: 0m)));
    }

    [Test]
    public void A_trade_the_buyer_started_adds_to_what_was_traded_and_to_what_was_bought()
    {
        TradedVolume trades = new TradedVolume(traded: 3m, bought: 1m).Add(quantity: 2m, bought: true);

        Assert.That(trades, Is.EqualTo(new TradedVolume(traded: 5m, bought: 3m)));
    }

    [Test]
    public void A_trade_the_seller_started_adds_only_to_what_was_traded()
    {
        TradedVolume trades = new TradedVolume(traded: 3m, bought: 1m).Add(quantity: 2m, bought: false);

        Assert.That(trades, Is.EqualTo(new TradedVolume(traded: 5m, bought: 1m)));
    }
}