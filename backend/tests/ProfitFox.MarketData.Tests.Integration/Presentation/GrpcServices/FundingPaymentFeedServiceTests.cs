using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using MediatR;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;
using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
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
///     The feed of funding payments: what it tells. What the feeds of points do the same way is checked in
///     <see cref="OpenInterestFeedServiceTests" />.
/// </summary>
[TestFixture]
public class FundingPaymentFeedServiceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app.Bybit.Requests.Clear();
        _services = IntegrationEnvironment.Services();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    private const int SecondsOfHour = 3600;

    // The app runs on the real clock, and a subscription catches up on a day at most: the last payment was made at the hour before
    // this one. The clock is read once: a second reading may be of the next hour, or of the next day.
    private static readonly DateTimeOffset LastHour = DateTimeOffset
        .FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / SecondsOfHour * SecondsOfHour)
        .AddHours(-1);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private MarketDataTestApplication _app = null!;
    private GrpcChannel _channel = null!;
    private FundingPaymentFeed.FundingPaymentFeedClient _client = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _channel = Channel(_app);
        _client = new FundingPaymentFeed.FundingPaymentFeedClient(_channel);
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
    private async Task<AsyncServerStreamingCall<FundingPaymentUpdate>> SubscribeAsync(SubscribeFundingPaymentRequest request)
    {
        AsyncServerStreamingCall<FundingPaymentUpdate> call = _client.Subscribe(request);
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        return call;
    }

    private static async Task<IReadOnlyList<FundingPaymentUpdate>> TakeAsync(AsyncServerStreamingCall<FundingPaymentUpdate> call, int count)
    {
        var updates = new List<FundingPaymentUpdate>();
        using var patience = new CancellationTokenSource(Patience);

        while (updates.Count < count && await call.ResponseStream.MoveNext(patience.Token))
        {
            updates.Add(call.ResponseStream.Current);
        }

        return updates;
    }

    // The series of BTC covering [from, until) with the payments.
    private Task StoreAsync(DateTimeOffset from, DateTimeOffset until, params FundingPayment[] payments)
    {
        return _services.GetRequiredService<IFundingPaymentSeriesRepository>()
            .SaveAsync(FundingPaymentSeries.Create(Btc.Id, from, until, payments), CancellationToken.None);
    }

    private async Task<LoadFundingPaymentsResult> LoadAsync(DateTimeOffset requestedAt)
    {
        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new LoadFundingPaymentsCommand(Btc.Id, requestedAt.AddDays(-30), requestedAt), CancellationToken.None);
    }

    [Test]
    public async Task A_payment_made_since_the_loading_last_looked_reaches_a_subscriber_and_a_page_of_history_does_not()
    {
        // The payments are loaded up to a moment after the hour before the last one; the history before a month ago is not.
        await StoreAsync(LastHour.AddDays(-10), LastHour.AddHours(-1).AddSeconds(15), new FundingPayment(LastHour.AddHours(-8), rate: 1m));
        using AsyncServerStreamingCall<FundingPaymentUpdate> call = await SubscribeAsync(new SubscribeFundingPaymentRequest());
        _app.Bybit
            .RespondJson(BybitAnswers.FundingPayments(BybitAnswers.FundingPayment("BTCUSDT", "-0.00118658", LastHour)))
            .RespondJson(BybitAnswers.FundingPayments(BybitAnswers.FundingPayment("BTCUSDT", "0.5", LastHour.AddDays(-20))));

        // Half a minute after the last hour: first what was paid since, then a page of the history.
        LoadFundingPaymentsResult paidSince = await LoadAsync(LastHour.AddSeconds(30));
        await LoadAsync(LastHour.AddSeconds(30));

        // A payment told after the two loads: it comes second only if the page of history told none.
        _app.Services.GetRequiredService<ILiveFeed<InstrumentFundingPaymentInternal>>()
            .Publish([new InstrumentFundingPaymentInternal(Btc.Id, LastHour.AddHours(1), FundingRate: 9m)]);
        IReadOnlyList<FundingPaymentUpdate> updates = await TakeAsync(call, count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(paidSince, Is.EqualTo(LoadFundingPaymentsResult.MoreLeft));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(2));
            Assert.That(updates[0].InstrumentId, Is.EqualTo(Btc.Id.ToString()));
            Assert.That(updates[0].Time.ToDateTimeOffset(), Is.EqualTo(LastHour));
            Assert.That(updates[0].FundingRate, Is.EqualTo("-0.00118658"));
            Assert.That(updates[1].FundingRate, Is.EqualTo("9"));
        });
    }

    [Test]
    public async Task A_subscription_from_further_back_than_the_setting_of_the_feed_tells_is_refused()
    {
        // An hour for this feed and a day for the other two: the service reads its own setting, not the one of another feed.
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting("FundingPaymentFeed:MaxCatchUpHours", "1"));
        using GrpcChannel channel = Channel(app);
        using AsyncServerStreamingCall<FundingPaymentUpdate> call = new FundingPaymentFeed.FundingPaymentFeedClient(channel).Subscribe(
            new SubscribeFundingPaymentRequest { Since = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-2)) });

        await Assert.ThatAsync(
            () => call.ResponseStream.MoveNext(CancellationToken.None).WaitAsync(Patience),
            Throws.TypeOf<RpcException>().With.Property(nameof(RpcException.StatusCode)).EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public async Task The_stored_payments_since_the_time_asked_come_first_and_the_live_ones_follow()
    {
        await StoreAsync(
            LastHour.AddDays(-1),
            LastHour.AddSeconds(15),
            new FundingPayment(LastHour.AddHours(-16), rate: 1m),
            new FundingPayment(LastHour.AddHours(-8), rate: 0.00000819m),
            new FundingPayment(LastHour, rate: -0.00118658m));

        using AsyncServerStreamingCall<FundingPaymentUpdate> call = await SubscribeAsync(
            new SubscribeFundingPaymentRequest { Since = Timestamp.FromDateTimeOffset(LastHour.AddHours(-8)) });

        _app.Services.GetRequiredService<ILiveFeed<InstrumentFundingPaymentInternal>>()
            .Publish([new InstrumentFundingPaymentInternal(Btc.Id, LastHour.AddHours(1), FundingRate: 0.0001m)]);
        IReadOnlyList<FundingPaymentUpdate> updates = await TakeAsync(call, count: 3);

        Assert.That(updates.Select(update => update.FundingRate), Is.EqualTo(new[] { "0.00000819", "-0.00118658", "0.0001" }));
    }
}