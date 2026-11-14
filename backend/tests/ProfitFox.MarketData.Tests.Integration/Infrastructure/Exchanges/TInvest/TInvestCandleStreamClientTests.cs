using Grpc.Core;

using Microsoft.Extensions.Logging.Abstractions;

using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Integration.Time;

using Tinkoff.InvestApi.V1;

using Candle = ProfitFox.MarketData.Domain.ValueObjects.Candles.Candle;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.TInvest;

[TestFixture]
public class TInvestCandleStreamClientTests
{
    [SetUp]
    public void SetUp()
    {
        _api = new StubGrpcInvoker();
        _time = new WatchedTimeProvider(Minute.AddSeconds(30));
        _client = Client(Token);
        _subscription = null;
        _closed = [];
        _forming = [];
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync();
        }
    }

    private const string Token = "test-token";

    private const string Sber = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private const string Gazp = "962e2a95-02a9-4171-abd7-aa198dbe643a";

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // The client tells the forming candles once a second.
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    // A lot of 10 shares of one, of 100 of the other.
    private static readonly Dictionary<string, decimal> Lots = new Dictionary<string, decimal> { [Sber] = 10m, [Gazp] = 100m };

    private StubGrpcInvoker _api = null!;
    private WatchedTimeProvider _time = null!;
    private TInvestCandleStreamClient _client = null!;
    private IAsyncDisposable? _subscription;
    private List<(string Uid, Candle Candle)> _closed = null!;
    private List<(string Uid, Candle Candle)> _forming = null!;

    private TInvestCandleStreamClient Client(string token)
    {
        return new TInvestCandleStreamClient(
            new MarketDataStreamService.MarketDataStreamServiceClient(_api),
            new FixedOptionsMonitor<TInvestApiSettings>(new TInvestApiSettings { Token = token }),
            _time,
            NullLogger<TInvestCandleStreamClient>.Instance);
    }

    private async Task SubscribeAsync(IReadOnlyDictionary<string, decimal>? lots = null)
    {
        _subscription = await _client.SubscribeAsync(
            lots ?? Lots,
            (uid, candle) => _closed.Add((uid, candle)),
            (uid, candle) => _forming.Add((uid, candle)),
            CancellationToken.None);
    }

    // The next stream the client opened, once it has sent its ping settings and its subscription.
    private async Task<IStubGrpcStream> OpenedAsync()
    {
        IStubGrpcStream stream = await _api.NextStreamAsync();
        await stream.SentAsync(count: 2);

        return stream;
    }

    // The two streams of a subscription just made: of closed candles and of forming ones.
    private async Task<(IStubGrpcStream Closed, IStubGrpcStream Forming)> StreamsAsync()
    {
        IStubGrpcStream[] streams = [await OpenedAsync(), await OpenedAsync()];

        return (streams.Single(stream => Asked(stream).WaitingClose), streams.Single(stream => !Asked(stream).WaitingClose));
    }

    // The clock goes on second by second until the client opens a stream again; fails when it has not in five seconds of real time.
    private async Task<IStubGrpcStream> ReopenedAsync()
    {
        DateTime until = DateTime.UtcNow + Patience;

        while (!_api.HasNextStream())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("No stream was opened again.");
            }

            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10);
        }

        return await OpenedAsync();
    }

    // The pause the client starts before it opens a stream again: its next wait that is not one for the silence of a stream.
    private async Task<TimeSpan> NextPauseAsync()
    {
        TimeSpan wait;

        do
        {
            wait = await _time.NextWaitAsync();
        }
        while (wait == TInvestCandleStreamClient.Silence);

        return wait;
    }

    private static SubscribeCandlesRequest Asked(IStubGrpcStream stream)
    {
        return ((MarketDataRequest)stream.Sent[1]).SubscribeCandlesRequest;
    }

    [Test]
    public async Task A_candle_T_Invest_told_wrong_is_dropped_and_the_stream_goes_on()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, _) = await StreamsAsync();

        // A high below the low.
        closed.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 299m, low: 300m, close: 300m, lots: 1, lotsBought: 1));
        closed.Tell(TInvestAnswers.StreamCandle(Gazp, Minute, open: 120m, high: 120m, low: 120m, close: 120m, lots: 3, lotsBought: 0));
        await closed.ReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_closed.Select(told => told.Uid), Is.EqualTo(new[] { Gazp }));
            Assert.That(_api.HasNextStream(), Is.False);
        });
    }

    [Test]
    public async Task A_closed_candle_comes_at_once_in_shares_with_its_buy_volume()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, _) = await StreamsAsync();

        closed.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 300.5m, low: 299.95m, close: 300.5m, lots: 12, lotsBought: 5));
        closed.Tell(TInvestAnswers.StreamCandle(Gazp, Minute, open: 120m, high: 120m, low: 120m, close: 120m, lots: 3, lotsBought: 0));
        await closed.ReadAsync();

        Assert.That(
            _closed,
            Is.EqualTo(
                new[]
                {
                    (Sber, new Candle(Minute, open: 300m, high: 300.5m, low: 299.95m, close: 300.5m, volume: 120m, buyVolume: 50m)),
                    (Gazp, new Candle(Minute, open: 120m, high: 120m, low: 120m, close: 120m, volume: 300m, buyVolume: 0m))
                }));
    }

    [Test]
    public async Task A_forming_candle_of_a_minute_already_closed_is_not_told()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, IStubGrpcStream forming) = await StreamsAsync();

        // One waits to be told when its minute closes, another of that minute comes late on the other stream.
        forming.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 300m, low: 300m, close: 300m, lots: 1, lotsBought: 1));
        await forming.ReadAsync();
        closed.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 301m, low: 300m, close: 301m, lots: 2, lotsBought: 1));
        await closed.ReadAsync();
        forming.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 301m, low: 300m, close: 301m, lots: 2, lotsBought: 1));
        forming.Tell(TInvestAnswers.StreamCandle(Sber, Minute.AddMinutes(1), open: 301m, high: 302m, low: 301m, close: 302m, lots: 4, lotsBought: 3));
        await forming.ReadAsync();

        await _time.PassPeriodAsync(Second);

        Assert.That(_forming.Select(told => told.Candle.OpenTime), Is.EqualTo(new[] { Minute.AddMinutes(1) }));
    }

    [Test]
    public async Task A_forming_candle_of_the_last_closed_minute_is_not_told_after_an_older_candle_closed_again()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, IStubGrpcStream forming) = await StreamsAsync();

        // The minute closed, then the one before it is told again, as after the stream of closed candles is back.
        closed.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 301m, low: 300m, close: 301m, lots: 2, lotsBought: 1));
        closed.Tell(TInvestAnswers.StreamCandle(Sber, Minute.AddMinutes(-1), open: 300m, high: 300m, low: 300m, close: 300m, lots: 1, lotsBought: 1));
        await closed.ReadAsync();
        forming.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 301m, low: 300m, close: 301m, lots: 2, lotsBought: 1));
        await forming.ReadAsync();

        await _time.PassPeriodAsync(Second);

        Assert.That(_forming, Is.Empty);
    }

    [Test]
    public async Task A_stream_that_breaks_is_opened_again_after_a_pause_and_subscribes_anew()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, _) = await StreamsAsync();

        closed.Break(StatusCode.Unavailable);
        bool openedAtOnce = _api.HasNextStream();
        IStubGrpcStream again = await ReopenedAsync();
        again.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 300m, low: 300m, close: 300m, lots: 1, lotsBought: 1));
        await again.ReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(openedAtOnce, Is.False);
            Assert.That(Asked(again).WaitingClose, Is.True);
            Assert.That(Asked(again).Instruments.Select(instrument => instrument.InstrumentId), Is.EquivalentTo(new[] { Sber, Gazp }));
            Assert.That(_closed.Single().Uid, Is.EqualTo(Sber));
        });
    }

    [Test]
    public async Task A_stream_that_ends_is_opened_again_and_subscribes_anew()
    {
        await SubscribeAsync();
        (_, IStubGrpcStream forming) = await StreamsAsync();

        forming.End();
        IStubGrpcStream again = await ReopenedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Asked(again).WaitingClose, Is.False);
            Assert.That(Asked(again).Instruments.Select(instrument => instrument.InstrumentId), Is.EquivalentTo(new[] { Sber, Gazp }));
        });
    }

    [Test]
    public async Task A_stream_that_keeps_breaking_is_opened_again_after_longer_pauses_until_it_tells_something()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, _) = await StreamsAsync();

        // The timer of the forming candles, started with the subscription, is not a pause.
        while (await _time.NextWaitAsync() != TimeSpan.FromSeconds(1))
        {
        }

        var pauses = new List<double>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            closed.Break(StatusCode.Unavailable);
            TimeSpan pause = await NextPauseAsync();
            pauses.Add(pause.TotalSeconds);
            _time.Advance(pause);
            closed = await OpenedAsync();
        }

        closed.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 300m, low: 300m, close: 300m, lots: 1, lotsBought: 1));
        await closed.ReadAsync();
        closed.Break(StatusCode.Unavailable);
        TimeSpan afterAnAnswer = await NextPauseAsync();

        Assert.Multiple(() =>
        {
            Assert.That(pauses, Is.EqualTo(new[] { 1d, 2d, 5d, 10d, 30d, 30d }));
            Assert.That(afterAnAnswer, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public async Task A_stream_that_is_silent_too_long_is_opened_again()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, IStubGrpcStream forming) = await StreamsAsync();
        await closed.ReadAsync();
        await forming.ReadAsync();

        _time.Advance(TInvestCandleStreamClient.Silence);
        IStubGrpcStream[] again = [await ReopenedAsync(), await ReopenedAsync()];

        Assert.That(again.Select(stream => Asked(stream).WaitingClose), Is.EquivalentTo(new[] { true, false }));
    }

    [Test]
    public async Task Disposing_the_subscription_closes_its_streams()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, IStubGrpcStream forming) = await StreamsAsync();

        await _subscription!.DisposeAsync();
        _subscription = null;

        Assert.Multiple(() =>
        {
            Assert.That(closed.Closed, Is.True);
            Assert.That(forming.Closed, Is.True);
        });
    }

    [Test]
    public async Task More_shares_than_a_stream_takes_open_further_streams()
    {
        var lots = Enumerable.Range(start: 0, TInvestCandleStreamClient.SharesPerStream + 1).ToDictionary(number => $"uid-{number}", _ => 1m);

        await SubscribeAsync(lots);
        IStubGrpcStream[] streams = [await OpenedAsync(), await OpenedAsync(), await OpenedAsync(), await OpenedAsync()];

        Assert.That(
            streams.Select(stream => (Asked(stream).WaitingClose, Asked(stream).Instruments.Count)),
            Is.EquivalentTo(new[] { (true, TInvestCandleStreamClient.SharesPerStream), (false, TInvestCandleStreamClient.SharesPerStream), (true, 1), (false, 1) }));
    }

    [Test]
    public async Task The_forming_candle_of_a_share_is_told_once_a_second_as_it_was_told_last()
    {
        await SubscribeAsync();
        (_, IStubGrpcStream forming) = await StreamsAsync();

        forming.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 300m, low: 300m, close: 300m, lots: 1, lotsBought: 1));
        forming.Tell(TInvestAnswers.StreamCandle(Sber, Minute, open: 300m, high: 301m, low: 300m, close: 301m, lots: 3, lotsBought: 1));
        await forming.ReadAsync();
        bool toldBeforeTheSecond = _forming.Count > 0;
        await _time.PassPeriodAsync(Second);
        await _time.PassPeriodAsync(Second);

        Assert.Multiple(() =>
        {
            Assert.That(toldBeforeTheSecond, Is.False);
            Assert.That(_forming, Is.EqualTo(new[] { (Sber, new Candle(Minute, open: 300m, high: 301m, low: 300m, close: 301m, volume: 30m, buyVolume: 10m)) }));
        });
    }

    [Test]
    public async Task Two_streams_are_opened_for_the_exchange_minute_candles_of_the_shares_with_the_token()
    {
        await SubscribeAsync();
        (IStubGrpcStream closed, IStubGrpcStream forming) = await StreamsAsync();

        Assert.Multiple(() =>
        {
            foreach (IStubGrpcStream stream in new[] { closed, forming })
            {
                Assert.That(stream.Method, Is.EqualTo("MarketDataStream"));
                Assert.That(stream.Headers.GetValue("authorization"), Is.EqualTo($"Bearer {Token}"));
                Assert.That(((MarketDataRequest)stream.Sent[0]).PingSettings.PingDelayMs, Is.EqualTo(30_000));
                Assert.That(Asked(stream).SubscriptionAction, Is.EqualTo(SubscriptionAction.Subscribe));
                Assert.That(Asked(stream).CandleSourceType, Is.EqualTo(GetCandlesRequest.Types.CandleSource.Exchange));
                Assert.That(Asked(stream).Instruments.Select(instrument => instrument.InstrumentId), Is.EquivalentTo(new[] { Sber, Gazp }));
                Assert.That(Asked(stream).Instruments.Select(instrument => instrument.Interval), Is.All.EqualTo(SubscriptionInterval.OneMinute));
            }
        });
    }

    [Test]
    public void Without_a_token_nothing_is_subscribed()
    {
        _client = Client(" ");

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<NotSupportedException>(() => SubscribeAsync());
            Assert.That(_api.HasNextStream(), Is.False);
        });
    }
}