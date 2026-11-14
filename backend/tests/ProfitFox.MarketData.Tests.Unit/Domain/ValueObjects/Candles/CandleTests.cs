using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Unit.Domain.ValueObjects.Candles;

[TestFixture]
public class CandleTests
{
    private static readonly DateTimeOffset OpenTime = new DateTimeOffset(year: 2026, month: 11, day: 13, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    [TestCase(0)]
    [TestCase(-1)]
    public void Non_positive_prices_are_rejected(decimal price)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new Candle(OpenTime, price, high: 10m, low: 1m, close: 5m, volume: 1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Candle(OpenTime, open: 5m, price, low: 1m, close: 5m, volume: 1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Candle(OpenTime, open: 5m, high: 10m, price, close: 5m, volume: 1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Candle(OpenTime, open: 5m, high: 10m, low: 1m, price, volume: 1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [TestCase(arg: 30)]
    [TestCase(arg: 0.001)]
    public void An_open_time_inside_a_minute_is_rejected(double seconds)
    {
        Assert.Throws<ArgumentException>(() => new Candle(OpenTime.AddSeconds(seconds), open: 5m, high: 10m, low: 1m, close: 5m, volume: 1m));
    }

    [TestCase(arg1: 0.5, arg2: 5)]
    [TestCase(arg1: 11, arg2: 5)]
    [TestCase(arg1: 5, arg2: 0.5)]
    [TestCase(arg1: 5, arg2: 11)]
    public void An_open_or_close_outside_the_low_and_high_is_rejected(decimal open, decimal close)
    {
        Assert.Throws<ArgumentException>(() => new Candle(OpenTime, open, high: 10m, low: 1m, close, volume: 1m));
    }

    [Test]
    public void A_low_above_the_high_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Candle(OpenTime, open: 5m, high: 1m, low: 10m, close: 5m, volume: 1m));
    }

    [Test]
    public void A_minute_without_trades_has_no_volume_and_no_turnover()
    {
        var candle = new Candle(OpenTime, open: 5m, high: 5m, low: 5m, close: 5m, volume: 0m, turnover: 0m);

        Assert.Multiple(() =>
        {
            Assert.That(candle.Volume, Is.Zero);
            Assert.That(candle.Turnover, Is.Zero);
        });
    }

    [Test]
    public void Negative_volume_or_turnover_is_rejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: -1m), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 1m, turnover: -1m),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void A_buy_volume_is_any_part_of_the_volume()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 3m, buyVolume: 0m).BuyVolume, Is.Zero);
            Assert.That(new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 3m, buyVolume: 3m).BuyVolume, Is.EqualTo(3m));
            Assert.That(new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 3m).BuyVolume, Is.Null);
        });
    }

    [Test]
    public void Trades_that_add_up_to_the_volume_tell_the_buy_volume()
    {
        var candle = new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 3m, turnover: 15m);

        Candle told = candle.WithBuyVolume(new TradedVolume(traded: 3m, bought: 2m));

        Assert.That(told, Is.EqualTo(new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 3m, turnover: 15m, buyVolume: 2m)));
    }

    [TestCase(arg: 2.9)]
    [TestCase(arg: 3.1)]
    public void Trades_that_do_not_add_up_to_the_volume_leave_the_candle_as_it_is(decimal traded)
    {
        var candle = new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 3m);

        Assert.That(candle.WithBuyVolume(new TradedVolume(traded, bought: 1m)), Is.SameAs(candle));
    }

    [Test]
    public void A_candle_without_volume_has_nothing_bought_when_no_trade_was_seen()
    {
        var candle = new Candle(OpenTime, open: 5m, high: 5m, low: 5m, close: 5m, volume: 0m);

        Assert.That(candle.WithBuyVolume(TradedVolume.None).BuyVolume, Is.Zero);
    }

    [TestCase(-1)]
    [TestCase(arg: 3.01)]
    public void A_buy_volume_below_zero_or_above_the_volume_is_rejected(decimal buyVolume)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Candle(OpenTime, open: 5m, high: 10m, low: 1m, close: 5m, volume: 3m, buyVolume: buyVolume));
    }
}