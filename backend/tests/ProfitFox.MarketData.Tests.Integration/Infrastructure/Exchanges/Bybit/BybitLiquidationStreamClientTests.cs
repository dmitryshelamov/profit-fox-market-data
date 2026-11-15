using System.Threading.Channels;

using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Liquidations;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Tests.Integration.Sockets;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitLiquidationStreamClientTests
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
                    SocketIndividualSubscriptionCombineTarget = 100,

                    // A test of a broken connection does not wait the five seconds the client waits by default.
                    ReconnectInterval = TimeSpan.FromMilliseconds(100)
                }),
            NullLoggerFactory.Instance);
        _stream = new BybitLiquidationStreamClient(_socket, NullLogger<BybitLiquidationStreamClient>.Instance);
        _liquidated = Channel.CreateUnbounded<BybitLinearLiquidation>();
        _lost = Channel.CreateUnbounded<IReadOnlyCollection<string>>();
        _restored = Channel.CreateUnbounded<IReadOnlyCollection<string>>();
    }

    [TearDown]
    public async Task TearDown()
    {
        _socket.Dispose();
        await _server.DisposeAsync();
    }

    private const string Linear = "/v5/public/linear";

    private static readonly DateTimeOffset Time = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 7, TimeSpan.Zero);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private FakeBybitSocketServer _server = null!;
    private BybitSocketClient _socket = null!;
    private BybitLiquidationStreamClient _stream = null!;
    private Channel<BybitLinearLiquidation> _liquidated = null!;
    private Channel<IReadOnlyCollection<string>> _lost = null!;
    private Channel<IReadOnlyCollection<string>> _restored = null!;

    private Task<IAsyncDisposable> SubscribeAsync(params string[] symbols)
    {
        return _stream.SubscribeAsync(
            symbols,
            liquidation => _liquidated.Writer.TryWrite(liquidation),
            lost => _lost.Writer.TryWrite(lost),
            restored => _restored.Writer.TryWrite(restored),
            CancellationToken.None);
    }

    [TestCase("Buy", arg2: true)]
    [TestCase("Sell", arg2: false)]
    public async Task A_liquidation_is_told_with_the_side_of_its_position_Buy_for_a_long_one(string side, bool ofLong)
    {
        await SubscribeAsync("BTCUSDT");

        await _server.PushAsync(Linear, "allLiquidation.BTCUSDT", FakeBybitSocketServer.Liquidation("BTCUSDT", Time, side, size: 0.25m, price: 101500.5m));

        Assert.That(
            await _liquidated.Reader.ReadAsync().AsTask().WaitAsync(Patience),
            Is.EqualTo(new BybitLinearLiquidation("BTCUSDT", Time, ofLong, Size: 0.25m, Price: 101500.5m)));
    }

    [Test]
    public async Task A_broken_connection_is_told_as_lost_and_as_restored_once_its_symbols_are_subscribed_to_again()
    {
        await SubscribeAsync("BTCUSDT", "ETHUSDT");

        _server.BreakConnections();

        IReadOnlyCollection<string> lost = await _lost.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        IReadOnlyCollection<string> restored = await _restored.Reader.ReadAsync().AsTask().WaitAsync(Patience);
        Assert.Multiple(() =>
        {
            Assert.That(lost, Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT" }));
            Assert.That(restored, Is.EqualTo(new[] { "BTCUSDT", "ETHUSDT" }));
            Assert.That(_server.Topics(Linear), Is.EquivalentTo(new[] { "allLiquidation.BTCUSDT", "allLiquidation.ETHUSDT" }));
        });
    }

    [Test]
    public async Task A_disposed_subscription_unsubscribes()
    {
        IAsyncDisposable subscription = await SubscribeAsync("BTCUSDT");

        await subscription.DisposeAsync();

        Assert.DoesNotThrowAsync(_server.WaitForNoTopicsAsync);
    }

    [Test]
    public async Task A_subscription_Bybit_refuses_fails_and_leaves_nothing_subscribed()
    {
        string[] symbols = [.. Enumerable.Range(start: 1, count: 12).Select(number => $"C{number}USDT")];
        _server.RefusedTopics["allLiquidation.C12USDT"] = true;

        Assert.ThrowsAsync<TransientExchangeException>(() => SubscribeAsync(symbols));

        await _server.WaitForNoTopicsAsync();
    }

    [Test]
    public async Task The_liquidations_of_one_push_are_each_told()
    {
        await SubscribeAsync("BTCUSDT");

        await _server.PushAsync(
            Linear,
            "allLiquidation.BTCUSDT",
            FakeBybitSocketServer.Liquidation("BTCUSDT", Time, "Buy", size: 1m, price: 100m),
            FakeBybitSocketServer.Liquidation("BTCUSDT", Time.AddMilliseconds(200), "Sell", size: 2m, price: 101m));

        BybitLinearLiquidation[] told =
        [
            await _liquidated.Reader.ReadAsync().AsTask().WaitAsync(Patience),
            await _liquidated.Reader.ReadAsync().AsTask().WaitAsync(Patience)
        ];
        Assert.That(
            told,
            Is.EqualTo(
                new[]
                {
                    new BybitLinearLiquidation("BTCUSDT", Time, OfLong: true, Size: 1m, Price: 100m),
                    new BybitLinearLiquidation("BTCUSDT", Time.AddMilliseconds(200), OfLong: false, Size: 2m, Price: 101m)
                }));
    }

    [Test]
    public async Task The_symbols_are_subscribed_to_ten_a_request_on_the_linear_stream()
    {
        string[] symbols = [.. Enumerable.Range(start: 1, count: 12).Select(number => $"C{number}USDT")];

        await SubscribeAsync(symbols);

        Assert.Multiple(() =>
        {
            Assert.That(_server.SubscribeRequests.Select(topics => topics.Length), Is.EqualTo(new[] { 10, 2 }));
            Assert.That(_server.Topics(Linear), Is.EquivalentTo(symbols.Select(symbol => $"allLiquidation.{symbol}")));
        });
    }
}