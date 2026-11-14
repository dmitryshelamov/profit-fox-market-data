using Grpc.Core;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest.Converters;

using Tinkoff.InvestApi.V1;

// The SDK has a Candle of its own, the one of its stream.
using Candle = ProfitFox.MarketData.Domain.ValueObjects.Candles.Candle;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The minute candles of the exchange from <c>MarketDataStream</c> of the Invest API. Two streams a subscription: one with
///     <c>waiting_close</c>, which tells a candle once its minute ended, and one without, which tells the candle of the minute that goes
///     on at every change; that one is passed on once a second. A stream takes at most <see cref="SharesPerStream" /> shares, more open
///     further streams. A stream that ends, breaks or is silent for <see cref="Silence" /> (it pings every 30 s) is opened again after
///     1, 2, 5, 10, then 30 s and subscribes anew. The token is read from the settings before every opening.
/// </summary>
internal sealed class TInvestCandleStreamClient(
    MarketDataStreamService.MarketDataStreamServiceClient streams,
    IOptionsMonitor<TInvestApiSettings> settings,
    TimeProvider time,
    ILogger<TInvestCandleStreamClient> logger) : ITInvestCandleStreamClient
{
    // T-Invest takes 300 subscriptions on a stream.
    internal const int SharesPerStream = 300;

    private const int PingMilliseconds = 30_000;

    internal static readonly TimeSpan Silence = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan FormingPeriod = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan[] Pauses =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public Task<IAsyncDisposable> SubscribeAsync(
        IReadOnlyDictionary<string, decimal> lotsByUid,
        Action<string, Candle> closed,
        Action<string, Candle> forming,
        CancellationToken cancellationToken)
    {
        // Fails here, before anything runs, when there is no token to open a stream with.
        TInvestMetadata.Of(settings.CurrentValue, "The candles of T-Invest are not streamed");

        var subscription = new Subscription(streams, settings, time, logger, lotsByUid, closed, forming);
        subscription.Start();

        return Task.FromResult<IAsyncDisposable>(subscription);
    }

    private sealed class Subscription(
        MarketDataStreamService.MarketDataStreamServiceClient streams,
        IOptionsMonitor<TInvestApiSettings> settings,
        TimeProvider time,
        ILogger logger,
        IReadOnlyDictionary<string, decimal> lotsByUid,
        Action<string, Candle> closed,
        Action<string, Candle> forming) : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        private readonly Lock _lock = new Lock();

        // The latest forming candle of a share since the last second, and the minute of its last closed one.
        private readonly Dictionary<string, Candle> _forming = [];

        private readonly Dictionary<string, DateTimeOffset> _closedMinutes = [];

        private readonly List<Task> _streams = [];

        private ITimer? _timer;

        public void Start()
        {
            foreach (string[] uids in lotsByUid.Keys.Chunk(SharesPerStream))
            {
                _streams.Add(RunAsync(uids, waitingClose: true));
                _streams.Add(RunAsync(uids, waitingClose: false));
            }

            _timer = time.CreateTimer(_ => TellForming(), state: null, FormingPeriod, FormingPeriod);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();

            if (_timer is not null)
            {
                await _timer.DisposeAsync();
            }

            await Task.WhenAll(_streams);
            _stop.Dispose();
        }

        // One stream of the shares, opened again whenever it is lost, until the subscription ends.
        private async Task RunAsync(string[] uids, bool waitingClose)
        {
            await Task.Yield();
            var failures = 0;

            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    Metadata headers = TInvestMetadata.Of(settings.CurrentValue, "The candles of T-Invest are not streamed");
                    using AsyncDuplexStreamingCall<MarketDataRequest, MarketDataResponse> stream =
                        streams.MarketDataStream(headers, cancellationToken: _stop.Token);

                    await stream.RequestStream.WriteAsync(
                        new MarketDataRequest { PingSettings = new PingDelaySettings { PingDelayMs = PingMilliseconds } },
                        _stop.Token);
                    await stream.RequestStream.WriteAsync(Subscribe(uids, waitingClose), _stop.Token);

                    // Silence longer than a few pings is a stream that died without a word.
                    while (await stream.ResponseStream.MoveNext(_stop.Token).WaitAsync(Silence, time, _stop.Token))
                    {
                        failures = 0;
                        Take(stream.ResponseStream.Current, waitingClose);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "A stream of {Kind} candles of T-Invest is lost; it is opened again.",
                        waitingClose ? "closed" : "forming");
                }

                try
                {
                    await Task.Delay(Pauses[Math.Min(failures++, Pauses.Length - 1)], time, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private static MarketDataRequest Subscribe(string[] uids, bool waitingClose)
        {
            return new MarketDataRequest
            {
                SubscribeCandlesRequest = new SubscribeCandlesRequest
                {
                    SubscriptionAction = SubscriptionAction.Subscribe,
                    WaitingClose = waitingClose,
                    CandleSourceType = GetCandlesRequest.Types.CandleSource.Exchange,
                    Instruments = { uids.Select(uid => new CandleInstrument { InstrumentId = uid, Interval = SubscriptionInterval.OneMinute }) }
                }
            };
        }

        private void Take(MarketDataResponse response, bool waitingClose)
        {
            if (response.SubscribeCandlesResponse is { } subscribed)
            {
                int refused = subscribed.CandlesSubscriptions.Count(candles => candles.SubscriptionStatus != SubscriptionStatus.Success);

                if (refused > 0)
                {
                    logger.LogWarning(
                        "T-Invest refused {Refused} of {Asked} subscriptions to {Kind} candles.",
                        refused,
                        subscribed.CandlesSubscriptions.Count,
                        waitingClose ? "closed" : "forming");
                }
            }

            if (response.Candle is not { } told || !lotsByUid.TryGetValue(told.InstrumentUid, out decimal lot))
            {
                return;
            }

            try
            {
                Candle candle = TInvestCandleConverter.ToDomain(told, lot);

                if (waitingClose)
                {
                    Close(told.InstrumentUid, candle);
                }
                else
                {
                    Keep(told.InstrumentUid, candle);
                }
            }
            catch (ArgumentException exception)
            {
                // A candle T-Invest told wrong: its minute is left to the loading of candles, the stream goes on.
                logger.LogError(exception, "A candle of {Uid} of the stream of T-Invest is dropped.", told.InstrumentUid);
            }
        }

        // The forming candle of the minute is done with: one that waited to be told, or comes late on the other stream, is not told
        // after the closed one.
        private void Close(string uid, Candle candle)
        {
            lock (_lock)
            {
                // A candle older than the last closed one, as T-Invest may tell again once a stream is back, does not bring its minute back.
                if (!_closedMinutes.TryGetValue(uid, out DateTimeOffset lastClosed) || candle.OpenTime > lastClosed)
                {
                    _closedMinutes[uid] = candle.OpenTime;
                }

                if (_forming.TryGetValue(uid, out Candle? waiting) && waiting.OpenTime <= candle.OpenTime)
                {
                    _forming.Remove(uid);
                }
            }

            closed(uid, candle);
        }

        private void Keep(string uid, Candle candle)
        {
            lock (_lock)
            {
                if (!_closedMinutes.TryGetValue(uid, out DateTimeOffset lastClosed) || candle.OpenTime > lastClosed)
                {
                    _forming[uid] = candle;
                }
            }
        }

        // Runs on the timer once a second: the latest forming candle of every share that changed since the last time.
        private void TellForming()
        {
            KeyValuePair<string, Candle>[] changed;

            lock (_lock)
            {
                changed = [.. _forming];
                _forming.Clear();
            }

            try
            {
                foreach ((string uid, Candle candle) in changed)
                {
                    forming(uid, candle);
                }
            }
            catch (Exception exception)
            {
                // A listener that failed: the candles of this second are dropped, the next second tells them anew.
                logger.LogError(exception, "The forming candles of the stream of T-Invest are dropped.");
            }
        }
    }
}