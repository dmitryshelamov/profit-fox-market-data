using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using MediatR;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.GrpcServices;

/// <summary>
///     The feed of the funding rates of coming payments: what it tells. What the feeds of points do the same way is checked in
///     <see cref="OpenInterestFeedServiceTests" />.
/// </summary>
[TestFixture]
public class FundingRateFeedServiceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app.Bybit.Requests.Clear();
        _services = IntegrationEnvironment.Services();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Futures], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    // The app runs on the real clock, and a subscription catches up on a day at most: the stored points are of an hour ago.
    private static readonly DateTimeOffset HourAgo = OpenInterestSeries.StartOfStep(DateTimeOffset.UtcNow.AddHours(-1));

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Futures = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateFutures());

    private MarketDataTestApplication _app = null!;
    private GrpcChannel _channel = null!;
    private FundingRateFeed.FundingRateFeedClient _client = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _channel = Channel(_app);
        _client = new FundingRateFeed.FundingRateFeedClient(_channel);
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
    private async Task<AsyncServerStreamingCall<FundingRateUpdate>> SubscribeAsync(SubscribeFundingRateRequest request)
    {
        AsyncServerStreamingCall<FundingRateUpdate> call = _client.Subscribe(request);
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        return call;
    }

    private static async Task<IReadOnlyList<FundingRateUpdate>> TakeAsync(AsyncServerStreamingCall<FundingRateUpdate> call, int count)
    {
        var updates = new List<FundingRateUpdate>();
        using var patience = new CancellationTokenSource(Patience);

        while (updates.Count < count && await call.ResponseStream.MoveNext(patience.Token))
        {
            updates.Add(call.ResponseStream.Current);
        }

        return updates;
    }

    [Test]
    public async Task A_snapshot_the_collection_saved_reaches_a_subscriber_with_the_rates_of_the_contracts_that_pay_funding()
    {
        using AsyncServerStreamingCall<FundingRateUpdate> call = await SubscribeAsync(new SubscribeFundingRateRequest());
        _app.Bybit.RespondJson(
            BybitAnswers.Tickers(
                "linear",
                BybitAnswers.LinearTicker("BTCUSDT", "52345.123", "-0.00012"),
                BybitAnswers.LinearTicker("BTCUSDT-25DEC26", "810.5", "")));

        await using (AsyncServiceScope scope = _app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CollectBybitTickersCommand(HourAgo), CancellationToken.None);
        }

        // A point told after the snapshot: it comes second only if the dated contract, which has no rate, gave none.
        _app.Services.GetRequiredService<ILiveFeed<InstrumentFundingRateInternal>>()
            .Publish([new InstrumentFundingRateInternal(Futures.Id, HourAgo.AddMinutes(5), FundingRate: 9m)]);
        IReadOnlyList<FundingRateUpdate> updates = await TakeAsync(call, count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(updates[0].InstrumentId, Is.EqualTo(Btc.Id.ToString()));
            Assert.That(updates[0].Time.ToDateTimeOffset(), Is.EqualTo(HourAgo));
            Assert.That(updates[0].FundingRate, Is.EqualTo("-0.00012"));
            Assert.That(updates[1].FundingRate, Is.EqualTo("9"));
        });
    }

    [Test]
    public async Task A_subscription_from_further_back_than_the_setting_of_the_feed_tells_is_refused()
    {
        // An hour for this feed and a day for the other two: the service reads its own setting, not the one of another feed.
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting("FundingRateFeed:MaxCatchUpHours", "1"));
        using GrpcChannel channel = Channel(app);
        using AsyncServerStreamingCall<FundingRateUpdate> call = new FundingRateFeed.FundingRateFeedClient(channel).Subscribe(
            new SubscribeFundingRateRequest { Since = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-2)) });

        await Assert.ThatAsync(
            () => call.ResponseStream.MoveNext(CancellationToken.None).WaitAsync(Patience),
            Throws.TypeOf<RpcException>().With.Property(nameof(RpcException.StatusCode)).EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task The_stored_rates_since_the_time_asked_come_first_and_the_live_ones_follow()
    {
        await _services.GetRequiredService<IFundingRateRepository>()
            .SaveAsync(
                [
                    new FundingRatePoint(Btc.Id, HourAgo.AddMinutes(-5), rate: 1m),
                    new FundingRatePoint(Btc.Id, HourAgo, rate: 0.00000331m),
                    new FundingRatePoint(Btc.Id, HourAgo.AddMinutes(5), rate: -0.00012m)
                ],
                CancellationToken.None);

        using AsyncServerStreamingCall<FundingRateUpdate> call = await SubscribeAsync(
            new SubscribeFundingRateRequest { Since = Timestamp.FromDateTimeOffset(HourAgo) });

        _app.Services.GetRequiredService<ILiveFeed<InstrumentFundingRateInternal>>()
            .Publish([new InstrumentFundingRateInternal(Btc.Id, HourAgo.AddMinutes(10), FundingRate: 0.0001m)]);
        IReadOnlyList<FundingRateUpdate> updates = await TakeAsync(call, count: 3);

        Assert.That(updates.Select(update => update.FundingRate), Is.EqualTo(new[] { "0.00000331", "-0.00012", "0.0001" }));
    }
}