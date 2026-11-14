using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NSubstitute;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

using Exchange = ProfitFox.MarketData.Domain.Entities.Instruments.Enums.Exchange;
using ProtoExchange = ProfitFox.MarketData.Presentation.Protos.Exchange;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.GrpcServices;

[TestFixture]
public class CandleFeedServiceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Linear, Share], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    // The app runs on the real clock, and a subscription catches up on a day at most: the stored candles are of an hour ago.
    private static readonly DateTimeOffset HourAgo = MinuteRange.StartOfMinute(DateTimeOffset.UtcNow.AddHours(-1));

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());

    private MarketDataTestApplication _app = null!;
    private GrpcChannel _channel = null!;
    private CandleFeed.CandleFeedClient _client = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _channel = Channel(_app);
        _client = new CandleFeed.CandleFeedClient(_channel);
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        _channel.Dispose();
        await _app.DisposeAsync();
    }

    private static GrpcChannel Channel(WebApplicationFactory<Program> app)
    {
        return GrpcChannel.ForAddress(app.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = new ResponseVersionHandler(app.Server.CreateHandler()) });
    }

    // The call once the subscription is in place: what is published from then on reaches it.
    private async Task<AsyncServerStreamingCall<CandleUpdate>> SubscribeAsync(SubscribeRequest request)
    {
        AsyncServerStreamingCall<CandleUpdate> call = _client.Subscribe(request);
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        return call;
    }

    private static async Task<IReadOnlyList<CandleUpdate>> TakeAsync(AsyncServerStreamingCall<CandleUpdate> call, int count)
    {
        var updates = new List<CandleUpdate>();
        using var patience = new CancellationTokenSource(Patience);

        while (updates.Count < count && await call.ResponseStream.MoveNext(patience.Token))
        {
            updates.Add(call.ResponseStream.Current);
        }

        return updates;
    }

    private void Publish(params LiveCandle[] candles)
    {
        _app.Services.GetRequiredService<ILiveCandleFeed>().Publish(candles);
    }

    private static LiveCandle Closed(Exchange exchange, Instrument instrument, Candle candle)
    {
        return new LiveCandle(exchange, instrument.Id, candle, Closed: true);
    }

    private async Task StoreAsync(Instrument instrument, params Candle[] candles)
    {
        var series = CandleSeries.Create(instrument.Id);
        series.Cover(new MinuteRange(HourAgo.AddHours(-1), HourAgo.AddMinutes(30)), candles);
        await _services.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
    }

    private static Candle CandleAt(DateTimeOffset openTime, decimal close = 5m)
    {
        return new Candle(openTime, open: 5m, high: 10m, low: 1m, close, volume: 1m);
    }

    [Test]
    public async Task A_candle_comes_with_its_values_as_text_and_without_what_is_not_known()
    {
        using AsyncServerStreamingCall<CandleUpdate> call = await SubscribeAsync(new SubscribeRequest());

        Publish(
            Closed(
                Exchange.Bybit,
                Linear,
                new Candle(
                    HourAgo,
                    open: 0.00001234m,
                    high: 0.0000124m,
                    low: 0.0000123m,
                    close: 0.00001238m,
                    volume: 1234567.89m,
                    turnover: 15.2771m,
                    buyVolume: 600000.5m)),
            Closed(Exchange.Bybit, Linear, CandleAt(HourAgo.AddMinutes(1))));

        IReadOnlyList<CandleUpdate> updates = await TakeAsync(call, count: 2);
        Assert.Multiple(() =>
        {
            Assert.That(
                updates[0],
                Is.EqualTo(
                    new CandleUpdate
                    {
                        InstrumentId = Linear.Id.ToString(),
                        Exchange = ProtoExchange.Bybit,
                        OpenTime = Timestamp.FromDateTimeOffset(HourAgo),
                        Open = "0.00001234",
                        High = "0.0000124",
                        Low = "0.0000123",
                        Close = "0.00001238",
                        Volume = "1234567.89",
                        Turnover = "15.2771",
                        BuyVolume = "600000.5",
                        Closed = true
                    }));
            Assert.That(updates[1].HasTurnover, Is.False);
            Assert.That(updates[1].HasBuyVolume, Is.False);
        });
    }

    [Test]
    public async Task A_caller_gets_the_candles_of_the_exchanges_it_asked_for_and_of_all_when_it_named_none()
    {
        using AsyncServerStreamingCall<CandleUpdate> all = await SubscribeAsync(new SubscribeRequest());
        using AsyncServerStreamingCall<CandleUpdate> shares = await SubscribeAsync(new SubscribeRequest { Exchanges = { ProtoExchange.Tinvest } });

        Publish(Closed(Exchange.Bybit, Linear, CandleAt(HourAgo)), Closed(Exchange.TInvest, Share, CandleAt(HourAgo)));

        IReadOnlyList<CandleUpdate> ofAll = await TakeAsync(all, count: 2);
        IReadOnlyList<CandleUpdate> ofShares = await TakeAsync(shares, count: 1);
        Assert.Multiple(() =>
        {
            Assert.That(ofAll.Select(update => update.Exchange), Is.EqualTo(new[] { ProtoExchange.Bybit, ProtoExchange.Tinvest }));
            Assert.That(ofShares.Single().InstrumentId, Is.EqualTo(Share.Id.ToString()));
        });
    }

    [Test]
    public async Task A_forming_candle_comes_not_closed_and_only_to_a_caller_that_asked_for_them()
    {
        using AsyncServerStreamingCall<CandleUpdate> withForming = await SubscribeAsync(new SubscribeRequest { Forming = true });
        using AsyncServerStreamingCall<CandleUpdate> closedOnly = await SubscribeAsync(new SubscribeRequest());
        var forming = new Candle(HourAgo, open: 5m, high: 8m, low: 4m, close: 7m, volume: 3m, turnover: 18m, buyVolume: 2m);

        Publish(new LiveCandle(Exchange.Bybit, Linear.Id, forming, Closed: false), Closed(Exchange.Bybit, Linear, CandleAt(HourAgo, close: 9m)));

        IReadOnlyList<CandleUpdate> ofForming = await TakeAsync(withForming, count: 2);
        IReadOnlyList<CandleUpdate> ofClosedOnly = await TakeAsync(closedOnly, count: 1);
        Assert.Multiple(() =>
        {
            Assert.That(ofForming.Select(update => (update.Closed, update.Close)), Is.EqualTo(new[] { (false, "7"), (true, "9") }));
            Assert.That(ofForming[0].BuyVolume, Is.EqualTo("2"));
            Assert.That(ofClosedOnly.Single().Closed, Is.True);
        });
    }

    [Test]
    public async Task A_subscriber_that_fell_behind_is_cut_off_with_resource_exhausted()
    {
        ILiveCandleSubscription cutOff = Substitute.For<ILiveCandleSubscription>();
        cutOff.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(_ => FallBehind());
        ILiveCandleFeed feed = Substitute.For<ILiveCandleFeed>();
        feed.Subscribe(Arg.Any<IReadOnlyCollection<Exchange>>(), Arg.Any<bool>()).Returns(cutOff);
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(feed)));
        using GrpcChannel channel = Channel(app);

        using AsyncServerStreamingCall<CandleUpdate> call = new CandleFeed.CandleFeedClient(channel).Subscribe(new SubscribeRequest());

        RpcException? failure = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.ResourceExhausted));
    }

    [Test]
    public void A_subscription_from_further_back_than_a_day_is_refused()
    {
        using AsyncServerStreamingCall<CandleUpdate> call = _client.Subscribe(
            new SubscribeRequest { Since = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-25)) });

        RpcException? failure = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void A_subscription_to_an_exchange_not_in_the_contract_is_refused()
    {
        using AsyncServerStreamingCall<CandleUpdate> call = _client.Subscribe(new SubscribeRequest { Exchanges = { ProtoExchange.Unspecified } });

        RpcException? failure = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task A_subscription_ends_with_unavailable_when_the_app_stops()
    {
        // An app of its own: the one of the other tests goes on.
        await using var app = new MarketDataTestApplication();
        using GrpcChannel channel = Channel(app);
        using AsyncServerStreamingCall<CandleUpdate> call = new CandleFeed.CandleFeedClient(channel).Subscribe(new SubscribeRequest());
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        app.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        // Awaited, not blocked on: this code goes on where the client told the headers, and the client reads the end of the call only
        // once it has that thread back.
        await Assert.ThatAsync(
            () => call.ResponseStream.MoveNext(CancellationToken.None).WaitAsync(Patience),
            Throws.TypeOf<RpcException>().With.Property(nameof(RpcException.StatusCode)).EqualTo(StatusCode.Unavailable));
    }

    [Test]
    public async Task An_app_whose_feed_catches_up_on_no_time_does_not_start()
    {
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting("CandleFeed:MaxCatchUpHours", "0"));

        // Program logs the failed check of the settings and ends, so the test host finds the app gone, whatever it throws then.
        Assert.That(() => _ = app.Server, Throws.Exception);
    }

    [Test]
    public async Task The_stored_candles_since_the_time_asked_come_first_and_the_live_ones_follow()
    {
        await StoreAsync(Linear, CandleAt(HourAgo.AddMinutes(-1)), CandleAt(HourAgo, close: 6m), CandleAt(HourAgo.AddMinutes(1), close: 7m));
        await StoreAsync(Share, CandleAt(HourAgo));

        using AsyncServerStreamingCall<CandleUpdate> call = await SubscribeAsync(
            new SubscribeRequest { Since = Timestamp.FromDateTimeOffset(HourAgo), Exchanges = { ProtoExchange.Bybit } });

        // Saved before any stored candle is read by the caller: it waits in the subscription and is not left out.
        Publish(Closed(Exchange.Bybit, Linear, CandleAt(HourAgo.AddMinutes(2), close: 8m)));
        IReadOnlyList<CandleUpdate> updates = await TakeAsync(call, count: 3);

        Assert.Multiple(() =>
        {
            Assert.That(updates.Select(update => update.Close), Is.EqualTo(new[] { "6", "7", "8" }));
            Assert.That(updates.Select(update => update.InstrumentId), Is.All.EqualTo(Linear.Id.ToString()));
        });
    }

    private static async IAsyncEnumerable<LiveCandle> FallBehind()
    {
        await Task.Yield();

        throw new SubscriberFellBehindException();
#pragma warning disable CS0162 // Unreachable code: the compiler needs a yield to make this an iterator.
        yield break;
#pragma warning restore CS0162
    }
}