using System.Threading.Channels;

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Tests.Integration.Sockets;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitCandleStreamClientTests
{
    [SetUp]
    public async Task SetUp()
    {
        _server = await FakeBybitSocketServer.StartAsync();
        _socket = new BybitSocketClient(
            Options.Create(
                new BybitSocketOptions
                {
                    Environment = BybitEnvironment.CreateCustom("fake", BybitEnvironment.Live.RestBaseAddress, _server.Address),
                    SocketIndividualSubscriptionCombineTarget = 100
                }),
            NullLoggerFactory.Instance);
        _stream = new BybitCandleStreamClient(_socket, NullLogger<BybitCandleStreamClient>.Instance);
        _closed = Channel.CreateUnbounded<(string Symbol, Candle Candle)>();
    }

    [TearDown]
    public async Task TearDown()
    {
        _socket.Dispose();
        await _server.DisposeAsync();
    }

    private const string Linear = "/v5/public/linear";

    private const string Spot = "/v5/public/spot";

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private FakeBybitSocketServer _server = null!;
    private BybitSocketClient _socket = null!;
    private BybitCandleStreamClient _stream = null!;
    private Channel<(string Symbol, Candle Candle)> _closed = null!;

    private Task<IAsyncDisposable> SubscribeAsync(BybitCategory category, params string[] symbols)
    {
        return _stream.SubscribeAsync(category, symbols, (symbol, candle) => _closed.Writer.TryWrite((symbol, candle)), CancellationToken.None);
    }

    private async Task<(string Symbol, Candle Candle)> ClosedAsync()
    {
        return await _closed.Reader.ReadAsync().AsTask().WaitAsync(Patience);
    }

    [Test]
    public async Task A_candle_comes_when_Bybit_confirms_it_and_not_while_its_minute_goes_on()
    {
        await SubscribeAsync(BybitCategory.Linear, "BTCUSDT");

        await _server.PushAsync(
            Linear,
            "kline.1.BTCUSDT",
            FakeBybitSocketServer.Kline(Minute, open: 5m, high: 5m, low: 5m, close: 5m, volume: 0m, turnover: 0m, confirm: false));
        await _server.PushAsync(Linear, "kline.1.BTCUSDT", FakeBybitSocketServer.Kline(Minute, open: 5m, high: 8m, low: 4m, close: 7m, volume: 0m, turnover: 0m));

        (string Symbol, Candle Candle) closed = await ClosedAsync();
        Assert.Multiple(() =>
        {
            Assert.That(closed.Symbol, Is.EqualTo("BTCUSDT"));
            Assert.That(closed.Candle, Is.EqualTo(new Candle(Minute, open: 5m, high: 8m, low: 4m, close: 7m, volume: 0m, turnover: 0m, buyVolume: 0m)));
            Assert.That(_closed.Reader.TryRead(out _), Is.False);
        });
    }

    [Test]
    public async Task A_candle_of_spot_comes_from_the_spot_stream()
    {
        await SubscribeAsync(BybitCategory.Spot, "BTCUSDT");

        await _server.PushAsync(Spot, "publicTrade.BTCUSDT", FakeBybitSocketServer.Trade("BTCUSDT", Minute, "Sell", quantity: 0.5m));
        await _server.PushAsync(Spot, "kline.1.BTCUSDT", FakeBybitSocketServer.Kline(Minute, open: 5m, high: 5m, low: 5m, close: 5m, volume: 0.5m, turnover: 2.5m));

        Assert.That((await ClosedAsync()).Candle.BuyVolume, Is.Zero);
    }

    [Test]
    public async Task A_candle_whose_trades_do_not_add_up_to_its_volume_has_no_buy_volume()
    {
        await SubscribeAsync(BybitCategory.Linear, "BTCUSDT");

        await _server.PushAsync(Linear, "publicTrade.BTCUSDT", FakeBybitSocketServer.Trade("BTCUSDT", Minute.AddSeconds(40), "Buy", quantity: 2m));
        await _server.PushAsync(Linear, "kline.1.BTCUSDT", FakeBybitSocketServer.Kline(Minute, open: 5m, high: 8m, low: 4m, close: 7m, volume: 9m, turnover: 54m));

        (string Symbol, Candle Candle) closed = await ClosedAsync();
        Assert.Multiple(() =>
        {
            Assert.That(closed.Candle.Volume, Is.EqualTo(9m));
            Assert.That(closed.Candle.BuyVolume, Is.Null);
        });
    }

    [Test]
    public async Task A_candle_whose_trades_come_after_it_is_told_again_with_its_buy_volume()
    {
        await SubscribeAsync(BybitCategory.Linear, "BTCUSDT");
        await _server.PushAsync(Linear, "publicTrade.BTCUSDT", FakeBybitSocketServer.Trade("BTCUSDT", Minute, "Buy", quantity: 2m));
        await _server.PushAsync(Linear, "kline.1.BTCUSDT", FakeBybitSocketServer.Kline(Minute, open: 5m, high: 8m, low: 4m, close: 7m, volume: 3m, turnover: 18m));
        (string Symbol, Candle Candle) first = await ClosedAsync();

        await _server.PushAsync(Linear, "publicTrade.BTCUSDT", FakeBybitSocketServer.Trade("BTCUSDT", Minute.AddSeconds(59), "Sell", quantity: 1m));

        (string Symbol, Candle Candle) again = await ClosedAsync();
        Assert.Multiple(() =>
        {
            Assert.That(first.Candle.BuyVolume, Is.Null);
            Assert.That(again.Symbol, Is.EqualTo("BTCUSDT"));
            Assert.That(again.Candle, Is.EqualTo(new Candle(Minute, open: 5m, high: 8m, low: 4m, close: 7m, volume: 3m, turnover: 18m, buyVolume: 2m)));
        });
    }

    [Test]
    public async Task A_disposed_subscription_unsubscribes()
    {
        IAsyncDisposable subscription = await SubscribeAsync(BybitCategory.Linear, "BTCUSDT");

        await subscription.DisposeAsync();

        Assert.DoesNotThrowAsync(_server.WaitForNoTopicsAsync);
    }

    [Test]
    public async Task A_subscription_Bybit_refuses_fails_and_leaves_nothing_subscribed()
    {
        _server.RefusedTopics["kline.1.ETHUSDT"] = true;

        Assert.ThrowsAsync<TransientExchangeException>(() => SubscribeAsync(BybitCategory.Linear, "ETHUSDT"));

        await _server.WaitForNoTopicsAsync();
    }

    [Test]
    public async Task The_buy_volume_of_a_candle_is_the_trades_of_its_minute_the_buyers_started()
    {
        await SubscribeAsync(BybitCategory.Linear, "BTCUSDT", "ETHUSDT");

        await _server.PushAsync(
            Linear,
            "publicTrade.BTCUSDT",
            FakeBybitSocketServer.Trade("BTCUSDT", Minute, "Buy", quantity: 2m),
            FakeBybitSocketServer.Trade("BTCUSDT", Minute.AddSeconds(59), "Sell", quantity: 1m),
            FakeBybitSocketServer.Trade("BTCUSDT", Minute.AddSeconds(60), "Buy", quantity: 4m));
        await _server.PushAsync(Linear, "publicTrade.ETHUSDT", FakeBybitSocketServer.Trade("ETHUSDT", Minute, "Buy", quantity: 10m));
        await _server.PushAsync(Linear, "kline.1.BTCUSDT", FakeBybitSocketServer.Kline(Minute, open: 5m, high: 8m, low: 4m, close: 7m, volume: 3m, turnover: 18m));

        (string Symbol, Candle Candle) closed = await ClosedAsync();
        Assert.Multiple(() =>
        {
            Assert.That(closed.Symbol, Is.EqualTo("BTCUSDT"));
            Assert.That(closed.Candle, Is.EqualTo(new Candle(Minute, open: 5m, high: 8m, low: 4m, close: 7m, volume: 3m, turnover: 18m, buyVolume: 2m)));
        });
    }

    [Test]
    public async Task The_symbols_are_subscribed_to_ten_a_request_trades_and_candles_apart()
    {
        string[] symbols = [.. Enumerable.Range(start: 1, count: 12).Select(number => $"C{number}USDT")];

        await SubscribeAsync(BybitCategory.Linear, symbols);

        Assert.Multiple(() =>
        {
            Assert.That(_server.SubscribeRequests.Select(topics => topics.Length), Is.EqualTo(new[] { 10, 10, 2, 2 }));
            Assert.That(
                _server.Topics(Linear),
                Is.EquivalentTo(symbols.Select(symbol => $"publicTrade.{symbol}").Concat(symbols.Select(symbol => $"kline.1.{symbol}"))));
            Assert.That(_server.Topics(Spot), Is.Empty);
        });
    }
}