using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using MediatR;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;
using ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;
using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.GrpcServices;

/// <summary>
///     The feed of liquidations. What the feeds of points do the same way, a caller that fell behind and the stop of the app among it,
///     is tested with the feed of open interest.
/// </summary>
[TestFixture]
public class LiquidationFeedServiceTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    // The app runs on the real clock, and a subscription catches up on a day at most: the stored minutes are of an hour ago.
    private static readonly DateTimeOffset HourAgo = MinuteRange.StartOfMinute(DateTimeOffset.UtcNow.AddHours(-1));

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // The ids put BTC before ETH among the minutes of one time.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private static readonly LiquidationSum Longs = new LiquidationSum(volume: 0.75m, turnover: 76125.5m, count: 3);

    private static readonly LiquidationSum Shorts = new LiquidationSum(volume: 0.00000331m, turnover: 0.33700m, count: 1);

    private MarketDataTestApplication _app = null!;
    private GrpcChannel _channel = null!;
    private LiquidationFeed.LiquidationFeedClient _client = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _channel = Channel(_app);
        _client = new LiquidationFeed.LiquidationFeedClient(_channel);
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
    private async Task<AsyncServerStreamingCall<LiquidationUpdate>> SubscribeAsync(SubscribeLiquidationsRequest request)
    {
        AsyncServerStreamingCall<LiquidationUpdate> call = _client.Subscribe(request);
        await call.ResponseHeadersAsync.WaitAsync(Patience);

        return call;
    }

    private static async Task<IReadOnlyList<LiquidationUpdate>> TakeAsync(AsyncServerStreamingCall<LiquidationUpdate> call, int count)
    {
        var updates = new List<LiquidationUpdate>();
        using var patience = new CancellationTokenSource(Patience);

        while (updates.Count < count && await call.ResponseStream.MoveNext(patience.Token))
        {
            updates.Add(call.ResponseStream.Current);
        }

        return updates;
    }

    // As the service that listens to the liquidations does: a command in a scope of its own.
    private async Task SendAsync(IRequest command)
    {
        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, CancellationToken.None);
    }

    [Test]
    public async Task A_minute_comes_with_its_instrument_its_time_its_mark_and_its_sums_as_text_exactly_as_stored()
    {
        using AsyncServerStreamingCall<LiquidationUpdate> call = await SubscribeAsync(new SubscribeLiquidationsRequest());

        _app.Services.GetRequiredService<ILiveFeed<InstrumentLiquidationMinuteInternal>>()
            .Publish(
            [
                new InstrumentLiquidationMinuteInternal(
                    Eth.Id,
                    HourAgo,
                    new LiquidationSumInternal(Volume: 0.75m, Turnover: 76125.5m, Count: 3),
                    new LiquidationSumInternal(Volume: 0.00000331m, Turnover: 0.33700m, Count: 1),
                    IsValid: false)
            ]);
        LiquidationUpdate update = (await TakeAsync(call, count: 1)).Single();

        Assert.That(
            update,
            Is.EqualTo(
                new LiquidationUpdate
                {
                    InstrumentId = Eth.Id.ToString(),
                    Time = Timestamp.FromDateTimeOffset(HourAgo),
                    LongVolume = "0.75",
                    LongTurnover = "76125.5",
                    LongCount = 3,
                    ShortVolume = "0.00000331",
                    ShortTurnover = "0.33700",
                    ShortCount = 1,
                    IsValid = false
                }));
    }

    [Test]
    public async Task A_minute_marked_not_valid_reaches_a_subscriber_once_and_not_again_when_it_is_marked_anew()
    {
        await SendAsync(new SaveLiquidationsCommand([new LiquidationMinute(Btc.Id, HourAgo, Longs, LiquidationSum.None)]));
        using AsyncServerStreamingCall<LiquidationUpdate> call = await SubscribeAsync(new SubscribeLiquidationsRequest());

        // The second marking finds the first minute not valid already: only the minute after it is told.
        await SendAsync(new InvalidateLiquidationsCommand([Btc.Id], HourAgo, HourAgo));
        await SendAsync(new InvalidateLiquidationsCommand([Btc.Id], HourAgo, HourAgo.AddMinutes(1)));
        IReadOnlyList<LiquidationUpdate> updates = await TakeAsync(call, count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(updates.Select(update => update.Time.ToDateTimeOffset()), Is.EqualTo(new[] { HourAgo, HourAgo.AddMinutes(1) }));
            Assert.That(updates.Select(update => update.IsValid), Is.All.False);
            Assert.That(updates.Select(update => update.LongVolume), Is.EqualTo(new[] { "0.75", "0" }));
        });
    }

    [Test]
    public async Task A_save_reaches_a_subscriber_with_all_that_is_stored_of_the_minute_not_with_what_was_added()
    {
        await SendAsync(new SaveLiquidationsCommand([new LiquidationMinute(Btc.Id, HourAgo, Longs, LiquidationSum.None)]));
        using AsyncServerStreamingCall<LiquidationUpdate> call = await SubscribeAsync(new SubscribeLiquidationsRequest());

        await SendAsync(new SaveLiquidationsCommand([new LiquidationMinute(Btc.Id, HourAgo, new LiquidationSum(volume: 0.25m, turnover: 25000m, count: 1), Shorts)]));
        LiquidationUpdate update = (await TakeAsync(call, count: 1)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(update.InstrumentId, Is.EqualTo(Btc.Id.ToString()));
            Assert.That(update.Time.ToDateTimeOffset(), Is.EqualTo(HourAgo));
            Assert.That((update.LongVolume, update.LongTurnover, update.LongCount), Is.EqualTo(("1.00", "101125.5", 4)));
            Assert.That((update.ShortVolume, update.ShortTurnover, update.ShortCount), Is.EqualTo(("0.00000331", "0.33700", 1)));
            Assert.That(update.IsValid, Is.True);
        });
    }

    [Test]
    public async Task The_stored_minutes_since_the_time_asked_come_first_oldest_first_and_the_live_ones_follow()
    {
        ILiquidationRepository liquidations = _services.GetRequiredService<ILiquidationRepository>();
        await liquidations.AddAsync(
            [
                new LiquidationMinute(Btc.Id, HourAgo.AddMinutes(-1), new LiquidationSum(volume: 1m, turnover: 1m, count: 1), LiquidationSum.None),
                new LiquidationMinute(Btc.Id, HourAgo, new LiquidationSum(volume: 2m, turnover: 2m, count: 1), LiquidationSum.None),
                new LiquidationMinute(Eth.Id, HourAgo, new LiquidationSum(volume: 3m, turnover: 3m, count: 1), LiquidationSum.None),
                new LiquidationMinute(Btc.Id, HourAgo.AddMinutes(1), new LiquidationSum(volume: 4m, turnover: 4m, count: 1), LiquidationSum.None)
            ],
            CancellationToken.None);

        using AsyncServerStreamingCall<LiquidationUpdate> call = await SubscribeAsync(
            new SubscribeLiquidationsRequest { Since = Timestamp.FromDateTimeOffset(HourAgo) });

        // Saved before any stored minute is read by the caller: it waits in the subscription and is not left out.
        var live = new LiquidationMinute(Btc.Id, HourAgo.AddMinutes(2), new LiquidationSum(volume: 5m, turnover: 5m, count: 1), LiquidationSum.None);
        await SendAsync(new SaveLiquidationsCommand([live]));
        IReadOnlyList<LiquidationUpdate> updates = await TakeAsync(call, count: 4);

        Assert.Multiple(() =>
        {
            Assert.That(updates.Select(update => update.LongVolume), Is.EqualTo(new[] { "2", "3", "4", "5" }));
            Assert.That(updates[1].InstrumentId, Is.EqualTo(Eth.Id.ToString()));
        });
    }
}