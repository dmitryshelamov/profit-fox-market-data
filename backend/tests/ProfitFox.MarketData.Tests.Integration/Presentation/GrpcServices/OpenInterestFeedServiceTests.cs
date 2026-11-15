using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using MediatR;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.GrpcServices;

/// <summary>
///     The feed of open interest, and with it what the three feeds of points do the same way: the catch-up, a caller that fell behind,
///     the stop of the app.
/// </summary>
[TestFixture]
public class OpenInterestFeedServiceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app.Bybit.Requests.Clear();
        _services = IntegrationEnvironment.Services();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    // The app runs on the real clock, and a subscription catches up on a day at most: the stored points are of an hour ago.
    private static readonly DateTimeOffset HourAgo = OpenInterestSeries.StartOfStep(DateTimeOffset.UtcNow.AddHours(-1));

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // The ids put BTC before ETH among the points of one time.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private MarketDataTestApplication _app = null!;
    private GrpcChannel _channel = null!;
    private OpenInterestFeed.OpenInterestFeedClient _client = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _channel = Channel(_app);
        _client = new OpenInterestFeed.OpenInterestFeedClient(_channel);
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
    private async Task<AsyncServerStreamingCall<OpenInterestUpdate>> SubscribeAsync(SubscribeOpenInterestRequest request)
    {
        AsyncServerStreamingCall<OpenInterestUpdate> call = _client.Subscribe(request);
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        return call;
    }

    private static async Task<IReadOnlyList<OpenInterestUpdate>> TakeAsync(AsyncServerStreamingCall<OpenInterestUpdate> call, int count)
    {
        var updates = new List<OpenInterestUpdate>();
        using var patience = new CancellationTokenSource(Patience);

        while (updates.Count < count && await call.ResponseStream.MoveNext(patience.Token))
        {
            updates.Add(call.ResponseStream.Current);
        }

        return updates;
    }

    private void Publish(params InstrumentOpenInterestInternal[] points)
    {
        _app.Services.GetRequiredService<ILiveFeed<InstrumentOpenInterestInternal>>().Publish(points);
    }

    private Task StoreAsync(params OpenInterestPoint[] points)
    {
        return _services.GetRequiredService<IOpenInterestRepository>().SaveAsync(points, CancellationToken.None);
    }

    private static async IAsyncEnumerable<InstrumentOpenInterestInternal> FallBehind()
    {
        await Task.Yield();

        throw new SubscriberFellBehindException();
#pragma warning disable CS0162 // Unreachable code: the compiler needs a yield to make this an iterator.
        yield break;
#pragma warning restore CS0162
    }

    [TestCase("OpenInterestFeed")]
    [TestCase("FundingRateFeed")]
    [TestCase("FundingPaymentFeed")]
    public async Task An_app_whose_feed_catches_up_on_no_time_does_not_start(string feed)
    {
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting($"{feed}:MaxCatchUpHours", "0"));

        // Program logs the failed check of the settings and ends, so the test host finds the app gone, whatever it throws then.
        Assert.That(() => _ = app.Server, Throws.Exception);
    }

    [Test]
    public async Task A_point_comes_with_its_instrument_its_time_and_its_value_as_text_exactly_as_stored()
    {
        using AsyncServerStreamingCall<OpenInterestUpdate> call = await SubscribeAsync(new SubscribeOpenInterestRequest());

        // The second value is as the history of the exchange tells it, with its zeros.
        Publish(
            new InstrumentOpenInterestInternal(Btc.Id, HourAgo, OpenInterest: 0.00001234m),
            new InstrumentOpenInterestInternal(Eth.Id, HourAgo, OpenInterest: 72241.05400000m));
        IReadOnlyList<OpenInterestUpdate> updates = await TakeAsync(call, count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(updates.Select(update => update.InstrumentId), Is.EqualTo(new[] { Btc.Id.ToString(), Eth.Id.ToString() }));
            Assert.That(updates.Select(update => update.Time.ToDateTimeOffset()), Is.All.EqualTo(HourAgo));
            Assert.That(updates.Select(update => update.OpenInterest), Is.EqualTo(new[] { "0.00001234", "72241.05400000" }));
        });
    }

    [Test]
    public async Task A_snapshot_the_collection_saved_reaches_a_subscriber()
    {
        using AsyncServerStreamingCall<OpenInterestUpdate> call = await SubscribeAsync(new SubscribeOpenInterestRequest());
        _app.Bybit.RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT", "52345.123", "0.00000331")));

        await using (AsyncServiceScope scope = _app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CollectBybitTickersCommand(HourAgo), CancellationToken.None);
        }

        OpenInterestUpdate update = (await TakeAsync(call, count: 1)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(update.InstrumentId, Is.EqualTo(Btc.Id.ToString()));
            Assert.That(update.Time.ToDateTimeOffset(), Is.EqualTo(HourAgo));
            Assert.That(update.OpenInterest, Is.EqualTo("52345.123"));
        });
    }

    [Test]
    public async Task A_subscriber_that_fell_behind_is_cut_off_with_resource_exhausted()
    {
        ILiveSubscription<InstrumentOpenInterestInternal> cutOff = Substitute.For<ILiveSubscription<InstrumentOpenInterestInternal>>();
        cutOff.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(_ => FallBehind());
        ILiveFeed<InstrumentOpenInterestInternal> feed = Substitute.For<ILiveFeed<InstrumentOpenInterestInternal>>();
        feed.Subscribe().Returns(cutOff);
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(feed)));
        using GrpcChannel channel = Channel(app);

        using AsyncServerStreamingCall<OpenInterestUpdate> call = new OpenInterestFeed.OpenInterestFeedClient(channel).Subscribe(new SubscribeOpenInterestRequest());

        RpcException? failure = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.ResourceExhausted));
    }

    [Test]
    public async Task A_subscription_ends_with_unavailable_when_the_app_stops()
    {
        // An app of its own: the one of the other tests goes on.
        await using var app = new MarketDataTestApplication();
        using GrpcChannel channel = Channel(app);
        using AsyncServerStreamingCall<OpenInterestUpdate> call = new OpenInterestFeed.OpenInterestFeedClient(channel).Subscribe(new SubscribeOpenInterestRequest());
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        app.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        // Awaited, not blocked on: this code goes on where the client told the headers, and the client reads the end of the call only
        // once it has that thread back.
        await Assert.ThatAsync(
            () => call.ResponseStream.MoveNext(CancellationToken.None).WaitAsync(Patience),
            Throws.TypeOf<RpcException>().With.Property(nameof(RpcException.StatusCode)).EqualTo(StatusCode.Unavailable));
    }

    [Test]
    public void A_subscription_from_further_back_than_a_day_is_refused()
    {
        using AsyncServerStreamingCall<OpenInterestUpdate> call = _client.Subscribe(
            new SubscribeOpenInterestRequest { Since = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-25)) });

        RpcException? failure = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task The_stored_points_since_the_time_asked_come_first_oldest_first_and_the_live_ones_follow()
    {
        await StoreAsync(
            new OpenInterestPoint(Btc.Id, HourAgo.AddMinutes(-5), value: 1m),
            new OpenInterestPoint(Btc.Id, HourAgo, value: 2m),
            new OpenInterestPoint(Eth.Id, HourAgo, value: 3m),
            new OpenInterestPoint(Btc.Id, HourAgo.AddMinutes(5), value: 4m));

        using AsyncServerStreamingCall<OpenInterestUpdate> call = await SubscribeAsync(
            new SubscribeOpenInterestRequest { Since = Timestamp.FromDateTimeOffset(HourAgo) });

        // Saved before any stored point is read by the caller: it waits in the subscription and is not left out.
        Publish(new InstrumentOpenInterestInternal(Btc.Id, HourAgo.AddMinutes(10), OpenInterest: 5m));
        IReadOnlyList<OpenInterestUpdate> updates = await TakeAsync(call, count: 4);

        Assert.Multiple(() =>
        {
            Assert.That(updates.Select(update => update.OpenInterest), Is.EqualTo(new[] { "2", "3", "4", "5" }));
            Assert.That(updates[1].InstrumentId, Is.EqualTo(Eth.Id.ToString()));
        });
    }
}