using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Presentation.GrpcServices;
using ProfitFox.MarketData.Presentation.Protos;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

using CandleInterval = ProfitFox.MarketData.Presentation.Protos.CandleInterval;
using Exchange = ProfitFox.MarketData.Presentation.Protos.Exchange;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.GrpcServices;

/// <summary>
///     The calls of history of the feeds, and with them what they do the same way: the coverage before the rows, the checks of a
///     request, the queue of the reads, the stop of the app.
/// </summary>
[TestFixture]
public class FeedHistoryTests
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

    // The app runs on the real clock, and its history holds past days only: the stored data is of three days ago.
    private static readonly DateTimeOffset Today = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

    private static readonly DateTimeOffset Day = Today.AddDays(-3);

    private static readonly DateTimeOffset Ten = Day.AddHours(10);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    // The ids put BTC before ETH.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private MarketDataTestApplication _app = null!;
    private GrpcChannel _channel = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _channel = Channel(_app);
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

    private static CandleHistoryRequest Candles(DateTimeOffset from, DateTimeOffset to, CandleInterval interval, params Instrument[] instruments)
    {
        var request = new CandleHistoryRequest { From = Timestamp.FromDateTimeOffset(from), To = Timestamp.FromDateTimeOffset(to), Interval = interval };
        request.InstrumentIds.AddRange(instruments.Select(instrument => instrument.Id.ToString()));

        return request;
    }

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(AsyncServerStreamingCall<T> call)
    {
        var items = new List<T>();
        using var patience = new CancellationTokenSource(Patience);

        while (await call.ResponseStream.MoveNext(patience.Token))
        {
            items.Add(call.ResponseStream.Current);
        }

        return items;
    }

    private async Task<IReadOnlyList<CandleHistoryItem>> ReadCandlesAsync(CandleHistoryRequest request)
    {
        using AsyncServerStreamingCall<CandleHistoryItem> call = new CandleFeed.CandleFeedClient(_channel).History(request);

        return await ReadAllAsync(call);
    }

    // Covered and settled: the history tells a minute of Bybit only once its buy volume will not change.
    private async Task StoreCandlesAsync(Instrument instrument, MinuteRange range, params Candle[] candles)
    {
        var series = CandleSeries.Create(instrument.Id);
        series.Cover(range, candles);
        series.Settle(range, []);
        await _services.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
    }

    private static TimeRange Range(DateTimeOffset from, DateTimeOffset until)
    {
        return new TimeRange { From = Timestamp.FromDateTimeOffset(from), Until = Timestamp.FromDateTimeOffset(until) };
    }

    private static Candle Flat(DateTimeOffset openTime)
    {
        return new Candle(openTime, open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m, turnover: 5m, buyVolume: 0.5m);
    }

    [Test]
    public async Task A_candle_comes_after_the_coverage_as_the_feed_tells_it()
    {
        await StoreCandlesAsync(
            Btc,
            new MinuteRange(Ten, Ten.AddMinutes(1)),
            new Candle(Ten, open: 0.00001234m, high: 0.0000124m, low: 0.0000123m, close: 0.00001238m, volume: 1234567.89m, turnover: 15.2771m, buyVolume: 600000.5m));

        IReadOnlyList<CandleHistoryItem> items = await ReadCandlesAsync(Candles(Day, Day.AddDays(1), CandleInterval.OneMinute, Btc, Eth));

        var btc = new InstrumentCoverage { InstrumentId = Btc.Id.ToString(), Ranges = { Range(Ten, Ten.AddMinutes(1)) } };
        var eth = new InstrumentCoverage { InstrumentId = Eth.Id.ToString() };
        var candle = new CandleUpdate
        {
            InstrumentId = Btc.Id.ToString(),
            Exchange = Exchange.Bybit,
            OpenTime = Timestamp.FromDateTimeOffset(Ten),
            Open = "0.00001234",
            High = "0.0000124",
            Low = "0.0000123",
            Close = "0.00001238",
            Volume = "1234567.89",
            Turnover = "15.2771",
            BuyVolume = "600000.5",
            Closed = true
        };
        Assert.That(
            items,
            Is.EqualTo(new[] { new CandleHistoryItem { Coverage = btc }, new CandleHistoryItem { Coverage = eth }, new CandleHistoryItem { Candle = candle } }));
    }

    [Test]
    public async Task Each_feed_of_points_tells_its_history()
    {
        DateTimeOffset until = Ten.AddMinutes(10);
        await _services.GetRequiredService<IOpenInterestSeriesRepository>()
            .SaveAsync(OpenInterestSeries.Create(Btc.Id, Ten, until, [new OpenInterestPoint(Btc.Id, Ten, value: 72241.05400000m)]), CancellationToken.None);
        await _services.GetRequiredService<ILongShortRatioSeriesRepository>()
            .SaveAsync(
                LongShortRatioSeries.Create(Btc.Id, Ten, until, [new LongShortRatioPoint(Btc.Id, Ten, longShare: 0.7302m, shortShare: 0.2698m)]),
                CancellationToken.None);
        await _services.GetRequiredService<IFundingPaymentSeriesRepository>()
            .SaveAsync(FundingPaymentSeries.Create(Btc.Id, Ten, until, [new FundingPayment(Ten, -0.00118658m)]), CancellationToken.None);
        var from = Timestamp.FromDateTimeOffset(Day);
        var to = Timestamp.FromDateTimeOffset(Today);
        string id = Btc.Id.ToString();

        IReadOnlyList<OpenInterestHistoryItem> openInterest = await ReadAllAsync(
            new OpenInterestFeed.OpenInterestFeedClient(_channel).History(new OpenInterestHistoryRequest { From = from, To = to, InstrumentIds = { id } }));
        IReadOnlyList<LongShortRatioHistoryItem> ratio = await ReadAllAsync(
            new LongShortRatioFeed.LongShortRatioFeedClient(_channel).History(new LongShortRatioHistoryRequest { From = from, To = to, InstrumentIds = { id } }));
        IReadOnlyList<FundingPaymentHistoryItem> payments = await ReadAllAsync(
            new FundingPaymentFeed.FundingPaymentFeedClient(_channel).History(new FundingPaymentHistoryRequest { From = from, To = to, InstrumentIds = { id } }));

        var coverage = new InstrumentCoverage { InstrumentId = id, Ranges = { Range(Ten, until) } };
        var time = Timestamp.FromDateTimeOffset(Ten);
        Assert.Multiple(() =>
        {
            Assert.That(
                openInterest,
                Is.EqualTo(
                    new[]
                    {
                        new OpenInterestHistoryItem { Coverage = coverage },
                        new OpenInterestHistoryItem { Point = new OpenInterestUpdate { InstrumentId = id, Time = time, OpenInterest = "72241.05400000" } }
                    }));
            Assert.That(
                ratio,
                Is.EqualTo(
                    new[]
                    {
                        new LongShortRatioHistoryItem { Coverage = coverage },
                        new LongShortRatioHistoryItem { Point = new LongShortRatioUpdate { InstrumentId = id, Time = time, Long = "0.7302", Short = "0.2698" } }
                    }));
            Assert.That(
                payments,
                Is.EqualTo(
                    new[]
                    {
                        new FundingPaymentHistoryItem { Coverage = coverage },
                        new FundingPaymentHistoryItem { Payment = new FundingPaymentUpdate { InstrumentId = id, Time = time, FundingRate = "-0.00118658" } }
                    }));
        });
    }

    [Test]
    public async Task A_covered_window_read_again_gives_the_same_messages()
    {
        await StoreCandlesAsync(Btc, new MinuteRange(Ten, Ten.AddHours(2)), Flat(Ten), Flat(Ten.AddMinutes(1)), Flat(Ten.AddHours(1)));
        await StoreCandlesAsync(Eth, new MinuteRange(Ten, Ten.AddHours(2)), Flat(Ten), Flat(Ten.AddHours(1).AddMinutes(30)));
        CandleHistoryRequest request = Candles(Ten, Ten.AddHours(2), CandleInterval.OneHour);

        IReadOnlyList<CandleHistoryItem> first = await ReadCandlesAsync(request);

        // Older history and newer minutes are loaded meanwhile, both outside the window.
        await StoreCandlesAsync(Btc, new MinuteRange(Ten.AddDays(-1), Ten), Flat(Ten.AddDays(-1)));
        await StoreCandlesAsync(Eth, new MinuteRange(Ten.AddHours(2), Ten.AddHours(3)), Flat(Ten.AddHours(2)));
        IReadOnlyList<CandleHistoryItem> second = await ReadCandlesAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(first.Count(item => item.ItemCase == CandleHistoryItem.ItemOneofCase.Candle), Is.EqualTo(4));
            Assert.That(second, Is.EqualTo(first));
        });
    }

    [Test]
    public async Task A_window_of_three_years_of_the_whole_market_is_taken()
    {
        await StoreCandlesAsync(Btc, new MinuteRange(Ten, Ten.AddMinutes(5)), Flat(Ten));

        IReadOnlyList<CandleHistoryItem> items = await ReadCandlesAsync(Candles(Today.AddDays(-1095), Today, CandleInterval.FiveMinutes));

        Assert.That(items.Count(item => item.ItemCase == CandleHistoryItem.ItemOneofCase.Candle), Is.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> WrongRequests()
    {
        yield return new TestCaseData(Candles(Today.AddDays(-1), Today.AddHours(1), CandleInterval.OneHour)).SetName("A window into today is refused");
        yield return new TestCaseData(Candles(Ten.AddMinutes(30), Ten.AddHours(2), CandleInterval.OneHour)).SetName("A window inside a candle is refused");
        yield return new TestCaseData(Candles(Ten, Ten.AddHours(1), CandleInterval.Unspecified)).SetName("A request without an interval is refused");
        yield return new TestCaseData(Candles(Ten, Ten, CandleInterval.OneHour)).SetName("A window that does not end after it starts is refused");
        yield return new TestCaseData(new CandleHistoryRequest { To = Timestamp.FromDateTimeOffset(Ten), Interval = CandleInterval.OneHour })
            .SetName("A window without its start is refused");

        CandleHistoryRequest unknownExchange = Candles(Ten, Ten.AddHours(1), CandleInterval.OneHour);
        unknownExchange.Exchanges.Add(Exchange.Unspecified);
        yield return new TestCaseData(unknownExchange).SetName("An exchange not of the contract is refused");

        CandleHistoryRequest wrongId = Candles(Ten, Ten.AddHours(1), CandleInterval.OneHour);
        wrongId.InstrumentIds.Add("BTCUSDT");
        yield return new TestCaseData(wrongId).SetName("An instrument id that is not an id is refused");
    }

    [TestCaseSource(nameof(WrongRequests))]
    public void A_wrong_request_is_refused_with_invalid_argument(CandleHistoryRequest request)
    {
        RpcException? failure = Assert.ThrowsAsync<RpcException>(() => ReadCandlesAsync(request));
        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void An_instrument_not_in_the_catalog_is_not_found()
    {
        CandleHistoryRequest request = Candles(Ten, Ten.AddHours(1), CandleInterval.OneHour);
        request.InstrumentIds.Add(Guid.Parse("0193a1f0-0000-7000-8000-0000000000ff").ToString());

        RpcException? failure = Assert.ThrowsAsync<RpcException>(() => ReadCandlesAsync(request));
        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public async Task A_call_over_the_places_tells_nothing_until_one_frees()
    {
        await StoreCandlesAsync(Btc, new MinuteRange(Ten, Ten.AddMinutes(1)), Flat(Ten));
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting("HistoryRead:MaxConcurrentReads", "1"));
        using GrpcChannel channel = Channel(app);
        IDisposable place = await app.Services.GetRequiredService<HistoryReadGate>().EnterAsync(CancellationToken.None);

        using AsyncServerStreamingCall<CandleHistoryItem> call = new CandleFeed.CandleFeedClient(channel).History(Candles(Day, Today, CandleInterval.OneMinute, Btc));
        Task<bool> first = call.ResponseStream.MoveNext(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        bool waited = !first.IsCompleted;
        place.Dispose();

        Assert.Multiple(async () =>
        {
            Assert.That(waited, Is.True);
            Assert.That(await first.WaitAsync(Patience), Is.True);
            Assert.That(call.ResponseStream.Current.ItemCase, Is.EqualTo(CandleHistoryItem.ItemOneofCase.Coverage));
        });
    }

    [Test]
    public async Task A_call_that_waits_for_its_place_ends_with_unavailable_when_the_app_stops()
    {
        // An app of its own: the one of the other tests goes on.
        await using var app = new MarketDataTestApplication();
        using GrpcChannel channel = Channel(app);
        HistoryReadGate gate = app.Services.GetRequiredService<HistoryReadGate>();
        using IDisposable first = await gate.EnterAsync(CancellationToken.None);
        using IDisposable second = await gate.EnterAsync(CancellationToken.None);
        using AsyncServerStreamingCall<CandleHistoryItem> call = new CandleFeed.CandleFeedClient(channel).History(Candles(Day, Today, CandleInterval.OneMinute, Btc));
        Task<bool> waiting = call.ResponseStream.MoveNext(CancellationToken.None);

        // The call reaches the server and waits for its place; stopped before, the app would refuse it as no call of its own.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.That(waiting.IsCompleted, Is.False);
        app.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        await Assert.ThatAsync(
            () => waiting.WaitAsync(Patience),
            Throws.TypeOf<RpcException>().With.Property(nameof(RpcException.StatusCode)).EqualTo(StatusCode.Unavailable));
    }

    [Test]
    public async Task An_app_whose_history_reads_none_at_once_does_not_start()
    {
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting("HistoryRead:MaxConcurrentReads", "0"));

        // Program logs the failed check of the settings and ends, so the test host finds the app gone, whatever it throws then.
        Assert.That(() => _ = app.Server, Throws.Exception);
    }

    [Test]
    public async Task An_app_whose_history_waits_for_storage_no_time_does_not_start()
    {
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting("HistoryRead:StatementTimeoutSeconds", "0"));

        // Program logs the failed check of the settings and ends, so the test host finds the app gone, whatever it throws then.
        Assert.That(() => _ = app.Server, Throws.Exception);
    }
}