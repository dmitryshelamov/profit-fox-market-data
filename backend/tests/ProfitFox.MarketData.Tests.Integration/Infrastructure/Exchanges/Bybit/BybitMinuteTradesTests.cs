using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitMinuteTradesTests
{
    [SetUp]
    public void SetUp()
    {
        _trades = new BybitMinuteTrades();
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private BybitMinuteTrades _trades = null!;

    private static Candle CandleOf(DateTimeOffset minute, decimal volume)
    {
        return CandleGenerator.Generate(minute, volume: volume);
    }

    [Test]
    public void A_candle_told_again_has_its_buy_volume_still()
    {
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 5m, bought: true);
        _trades.Close("BTCUSDT", CandleOf(Minute, volume: 2m));

        Assert.That(_trades.Close("BTCUSDT", CandleOf(Minute, volume: 2m)).BuyVolume, Is.EqualTo(2m));
    }

    [Test]
    public void A_forming_candle_is_built_from_the_trades_of_its_minute_and_opens_at_its_first_trade_while_the_close_before_is_not_known()
    {
        _trades.Watch(["BTCUSDT"], Minute.AddSeconds(-30));
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 100m, bought: true);
        _trades.Add("BTCUSDT", Minute.AddSeconds(1), quantity: 1m, price: 103m, bought: false);
        _trades.Add("BTCUSDT", Minute.AddSeconds(2), quantity: 0.5m, price: 99m, bought: true);

        Assert.That(
            _trades.TakeForming(),
            Is.EqualTo(new[] { ("BTCUSDT", new Candle(Minute, open: 100m, high: 103m, low: 99m, close: 99m, volume: 3.5m, turnover: 352.5m, buyVolume: 2.5m)) }));
    }

    [Test]
    public void A_forming_candle_is_told_again_only_when_its_minute_traded_again()
    {
        _trades.Watch(["BTCUSDT"], Minute);
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 100m, bought: true);
        _trades.TakeForming();

        IReadOnlyList<(string Symbol, Candle Candle)> untouched = _trades.TakeForming();
        _trades.Add("BTCUSDT", Minute.AddSeconds(5), quantity: 1m, price: 101m, bought: false);
        IReadOnlyList<(string Symbol, Candle Candle)> again = _trades.TakeForming();

        Assert.Multiple(() =>
        {
            Assert.That(untouched, Is.Empty);
            Assert.That(again.Single().Candle, Is.EqualTo(new Candle(Minute, open: 100m, high: 101m, low: 100m, close: 101m, volume: 3m, turnover: 301m, buyVolume: 2m)));
        });
    }

    [Test]
    public void A_forming_candle_opens_at_the_close_of_the_minute_before_once_that_is_known()
    {
        _trades.Watch(["BTCUSDT"], Minute.AddMinutes(-1));
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 7m, bought: true);
        _trades.TakeForming();

        // The candle of the minute before closes at 5: the forming one is told anew, opening there.
        _trades.Close("BTCUSDT", CandleGenerator.Generate(Minute.AddMinutes(-1), close: 5m, volume: 0m));

        Assert.That(
            _trades.TakeForming().Single().Candle,
            Is.EqualTo(new Candle(Minute, open: 5m, high: 7m, low: 5m, close: 7m, volume: 2m, turnover: 14m, buyVolume: 2m)));
    }

    [Test]
    public void A_late_trade_that_completes_the_closed_minute_gives_its_candle_with_the_buy_volume()
    {
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 5m, bought: true);
        Candle closed = _trades.Close("BTCUSDT", CandleOf(Minute, volume: 3.5m));

        Candle? afterOne = _trades.Add("BTCUSDT", Minute.AddSeconds(59), quantity: 1m, price: 5m, bought: false);
        Candle? afterBoth = _trades.Add("BTCUSDT", Minute.AddSeconds(59), quantity: 0.5m, price: 5m, bought: true);

        Assert.Multiple(() =>
        {
            Assert.That(closed.BuyVolume, Is.Null);
            Assert.That(afterOne, Is.Null);
            Assert.That(afterBoth, Is.EqualTo(CandleGenerator.Generate(Minute, volume: 3.5m, buyVolume: 2.5m)));
        });
    }

    [Test]
    public void A_minute_without_trades_and_without_volume_has_nothing_bought()
    {
        _trades.Add("BTCUSDT", Minute.AddMinutes(-1), quantity: 2m, price: 5m, bought: true);

        Assert.Multiple(() =>
        {
            Assert.That(_trades.Close("BTCUSDT", CandleOf(Minute, volume: 0m)).BuyVolume, Is.Zero);
            Assert.That(_trades.Close("ETHUSDT", CandleOf(Minute, volume: 0m)).BuyVolume, Is.Zero);
        });
    }

    [Test]
    public void A_minute_not_watched_from_its_start_or_closed_already_gives_no_forming_candle()
    {
        _trades.Watch(["BTCUSDT"], Minute.AddSeconds(10));
        _trades.Add("BTCUSDT", Minute.AddSeconds(20), quantity: 2m, price: 5m, bought: true);
        _trades.Add("ETHUSDT", Minute.AddMinutes(1), quantity: 2m, price: 5m, bought: true);
        IReadOnlyList<(string Symbol, Candle Candle)> seenInPart = _trades.TakeForming();

        _trades.Add("BTCUSDT", Minute.AddMinutes(1), quantity: 2m, price: 5m, bought: true);
        _trades.Add("BTCUSDT", Minute.AddMinutes(2), quantity: 3m, price: 5m, bought: true);
        _trades.Close("BTCUSDT", CandleOf(Minute.AddMinutes(1), volume: 2m));
        IReadOnlyList<(string Symbol, Candle Candle)> watched = _trades.TakeForming();

        Assert.Multiple(() =>
        {
            // The minute BTCUSDT was first seen in gives none, nor does ETHUSDT, which was never said to be watched.
            Assert.That(seenInPart, Is.Empty);

            // The minute that closed meanwhile gives none either: only the one that goes on.
            Assert.That(watched.Select(forming => forming.Candle.OpenTime), Is.EqualTo(new[] { Minute.AddMinutes(2) }));
        });
    }

    [Test]
    public void A_trade_of_a_minute_before_the_last_closed_one_is_not_counted()
    {
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 5m, bought: true);
        _trades.Close("BTCUSDT", CandleOf(Minute, volume: 3m));
        _trades.Close("BTCUSDT", CandleOf(Minute.AddMinutes(1), volume: 0m));

        Assert.Multiple(() =>
        {
            Assert.That(_trades.Add("BTCUSDT", Minute, quantity: 1m, price: 5m, bought: false), Is.Null);
            Assert.That(_trades.Close("BTCUSDT", CandleOf(Minute, volume: 3m)).BuyVolume, Is.Null);
        });
    }

    [Test]
    public void A_trade_of_the_minute_that_goes_on_gives_no_candle()
    {
        _trades.Close("BTCUSDT", CandleOf(Minute, volume: 4m));

        Assert.That(_trades.Add("BTCUSDT", Minute.AddMinutes(1), quantity: 4m, price: 5m, bought: true), Is.Null);
    }

    [Test]
    public void The_trades_of_a_minute_that_add_up_to_its_volume_tell_how_much_was_bought()
    {
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 5m, bought: true);
        _trades.Add("BTCUSDT", Minute.AddSeconds(30), quantity: 1.5m, price: 5m, bought: false);
        _trades.Add("BTCUSDT", Minute.AddSeconds(59.999), quantity: 0.25m, price: 5m, bought: true);

        Assert.That(_trades.Close("BTCUSDT", CandleOf(Minute, volume: 3.75m)), Is.EqualTo(CandleGenerator.Generate(Minute, volume: 3.75m, buyVolume: 2.25m)));
    }

    [Test]
    public void The_trades_of_the_next_minute_and_of_another_symbol_are_counted_apart()
    {
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 5m, bought: true);
        _trades.Add("BTCUSDT", Minute.AddMinutes(1), quantity: 4m, price: 5m, bought: false);
        _trades.Add("ETHUSDT", Minute, quantity: 7m, price: 5m, bought: true);

        Assert.Multiple(() =>
        {
            Assert.That(_trades.Close("BTCUSDT", CandleOf(Minute, volume: 2m)).BuyVolume, Is.EqualTo(2m));
            Assert.That(_trades.Close("BTCUSDT", CandleOf(Minute.AddMinutes(1), volume: 4m)).BuyVolume, Is.Zero);
            Assert.That(_trades.Close("ETHUSDT", CandleOf(Minute, volume: 7m)).BuyVolume, Is.EqualTo(7m));
        });
    }

    [TestCase(arg: 3)]
    [TestCase(arg: 1.5)]
    public void Trades_that_do_not_add_up_to_the_volume_do_not_tell_how_much_was_bought(decimal volume)
    {
        _trades.Add("BTCUSDT", Minute, quantity: 2m, price: 5m, bought: true);

        Assert.Multiple(() =>
        {
            Assert.That(_trades.Close("BTCUSDT", CandleOf(Minute, volume)).BuyVolume, Is.Null);
            Assert.That(_trades.Close("ETHUSDT", CandleOf(Minute, volume)).BuyVolume, Is.Null);
        });
    }
}