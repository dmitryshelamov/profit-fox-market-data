using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using MediatR;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.LongShortRatio.LoadLongShortRatio;
using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.GrpcServices;

/// <summary>
///     The feed of the long/short ratio. What the feeds of points do the same way, a caller that fell behind and the stop of the app
///     among it, is tested with the feed of open interest.
/// </summary>
[TestFixture]
public class LongShortRatioFeedServiceTests
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
    private static readonly DateTimeOffset HourAgo = LongShortRatioSeries.StartOfStep(DateTimeOffset.UtcNow.AddHours(-1));

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
    private LongShortRatioFeed.LongShortRatioFeedClient _client = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _channel = Channel(_app);
        _client = new LongShortRatioFeed.LongShortRatioFeedClient(_channel);
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
    private async Task<AsyncServerStreamingCall<LongShortRatioUpdate>> SubscribeAsync(SubscribeLongShortRatioRequest request)
    {
        AsyncServerStreamingCall<LongShortRatioUpdate> call = _client.Subscribe(request);
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        return call;
    }

    private static async Task<IReadOnlyList<LongShortRatioUpdate>> TakeAsync(AsyncServerStreamingCall<LongShortRatioUpdate> call, int count)
    {
        var updates = new List<LongShortRatioUpdate>();
        using var patience = new CancellationTokenSource(Patience);

        while (updates.Count < count && await call.ResponseStream.MoveNext(patience.Token))
        {
            updates.Add(call.ResponseStream.Current);
        }

        return updates;
    }

    private void Publish(params InstrumentLongShortRatioInternal[] points)
    {
        _app.Services.GetRequiredService<ILiveFeed<InstrumentLongShortRatioInternal>>().Publish(points);
    }

    // A series of the instrument that covers [from, until) with the points.
    private Task LoadAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset until, params (DateTimeOffset Time, decimal Long)[] points)
    {
        return _services.GetRequiredService<ILongShortRatioSeriesRepository>()
            .SaveAsync(
                LongShortRatioSeries.Create(
                    instrument.Id,
                    from,
                    until,
                    [.. points.Select(point => new LongShortRatioPoint(instrument.Id, point.Time, point.Long, 1m - point.Long))]),
                CancellationToken.None);
    }

    [Test]
    public async Task A_point_comes_with_its_instrument_its_time_and_its_shares_as_text_exactly_as_stored()
    {
        using AsyncServerStreamingCall<LongShortRatioUpdate> call = await SubscribeAsync(new SubscribeLongShortRatioRequest());

        Publish(new InstrumentLongShortRatioInternal(Eth.Id, HourAgo, Long: 0.7302m, Short: 0.2698m));
        LongShortRatioUpdate update = (await TakeAsync(call, count: 1)).Single();

        Assert.That(
            update,
            Is.EqualTo(
                new LongShortRatioUpdate
                {
                    InstrumentId = Eth.Id.ToString(),
                    Time = Timestamp.FromDateTimeOffset(HourAgo),
                    Long = "0.7302",
                    Short = "0.2698"
                }));
    }

    [Test]
    public async Task The_points_a_round_loaded_after_the_stored_ones_reach_a_subscriber_and_a_page_of_history_does_not()
    {
        // The series covers the hour up to an hour ago; Bybit has two steps told since, and a step of history before the series.
        await LoadAsync(Btc, HourAgo.AddHours(-1), HourAgo);
        using AsyncServerStreamingCall<LongShortRatioUpdate> call = await SubscribeAsync(new SubscribeLongShortRatioRequest());
        _app.Bybit
            .RespondJson(BybitAnswers.LongShortRatioHistory(BybitAnswers.LongShortRatioPoint("0.1", "0.9", HourAgo.AddHours(-1).AddMinutes(-5))))
            .RespondJson(
                BybitAnswers.LongShortRatioHistory(
                    BybitAnswers.LongShortRatioPoint("0.726", "0.274", HourAgo.AddMinutes(5)),
                    BybitAnswers.LongShortRatioPoint("0.7302", "0.2698", HourAgo)));

        await using (AsyncServiceScope scope = _app.Services.CreateAsyncScope())
        {
            ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

            // Thirteen minutes after the hour: the page of history, five minutes back, comes first while the steps of :00 and :05 are
            // less than an hour old; then those two steps.
            var load = new LoadLongShortRatioCommand(Btc.Id, HourAgo.AddHours(-1).AddMinutes(-5), HourAgo.AddMinutes(8));
            await sender.Send(load, CancellationToken.None);
            await sender.Send(load, CancellationToken.None);
        }

        Publish(new InstrumentLongShortRatioInternal(Eth.Id, HourAgo, Long: 0.5m, Short: 0.5m));
        IReadOnlyList<LongShortRatioUpdate> updates = await TakeAsync(call, count: 3);

        Assert.Multiple(() =>
        {
            // The two new points of BTC, then the point published after both pages were loaded: the page of history told nothing.
            Assert.That(updates.Select(update => update.Long), Is.EqualTo(new[] { "0.7302", "0.726", "0.5" }));
            Assert.That(updates.Select(update => update.InstrumentId), Is.EqualTo(new[] { Btc.Id.ToString(), Btc.Id.ToString(), Eth.Id.ToString() }));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task The_stored_points_since_the_time_asked_come_first_oldest_first_and_the_live_ones_follow()
    {
        await LoadAsync(Btc, HourAgo.AddMinutes(-5), HourAgo.AddMinutes(10), (HourAgo.AddMinutes(-5), 0.1m), (HourAgo, 0.2m), (HourAgo.AddMinutes(5), 0.4m));
        await LoadAsync(Eth, HourAgo, HourAgo.AddMinutes(5), (HourAgo, 0.3m));

        using AsyncServerStreamingCall<LongShortRatioUpdate> call = await SubscribeAsync(
            new SubscribeLongShortRatioRequest { Since = Timestamp.FromDateTimeOffset(HourAgo) });

        // Saved before any stored point is read by the caller: it waits in the subscription and is not left out.
        Publish(new InstrumentLongShortRatioInternal(Btc.Id, HourAgo.AddMinutes(10), Long: 0.5m, Short: 0.5m));
        IReadOnlyList<LongShortRatioUpdate> updates = await TakeAsync(call, count: 4);

        Assert.Multiple(() =>
        {
            Assert.That(updates.Select(update => update.Long), Is.EqualTo(new[] { "0.2", "0.3", "0.4", "0.5" }));
            Assert.That(updates[1].InstrumentId, Is.EqualTo(Eth.Id.ToString()));
        });
    }
}