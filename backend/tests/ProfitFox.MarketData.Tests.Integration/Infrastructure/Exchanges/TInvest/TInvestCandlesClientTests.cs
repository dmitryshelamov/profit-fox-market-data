using Grpc.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Tests.Integration.Grpc;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

using Tinkoff.InvestApi.V1;

using Candle = ProfitFox.MarketData.Domain.ValueObjects.Candles.Candle;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.TInvest;

[TestFixture]
public class TInvestCandlesClientTests
{
    [SetUp]
    public void SetUp()
    {
        _api = new StubGrpcInvoker();
        _candles = Candles(Token);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    private const string Token = "test-token";

    private const string SberUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 7, minute: 0, second: 0, TimeSpan.Zero);

    // A lot of 10 shares.
    private static readonly TInvestListedShare Sber = TInvestListedShareGenerator.Generate(uid: SberUid);

    private StubGrpcInvoker _api = null!;
    private ServiceProvider _services = null!;
    private TInvestCandlesClient _candles = null!;

    private TInvestCandlesClient Candles(string token)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Token"] = token })
            .Build();
        _services = new ServiceCollection().Configure<TInvestApiSettings>(configuration).BuildServiceProvider();

        return new TInvestCandlesClient(
            new MarketDataService.MarketDataServiceClient(_api),
            new InstrumentsService.InstrumentsServiceClient(_api),
            _services.GetRequiredService<IOptionsMonitor<TInvestApiSettings>>(),
            TimeProvider.System);
    }

    private static MinuteRange Day()
    {
        return new MinuteRange(Start, Start.AddDays(1));
    }

    [Test]
    public void A_failed_call_fails()
    {
        _api.Fail("GetCandles", StatusCode.ResourceExhausted);

        RpcException? failure = Assert.ThrowsAsync<RpcException>(() => _candles.GetAsync(Sber, Day(), CancellationToken.None));

        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.ResourceExhausted));
    }

    [Test]
    public async Task A_share_T_Invest_tells_no_first_minute_candle_of_has_no_known_start()
    {
        _api.Respond("ShareBy", TInvestAnswers.ShareBy(SberUid, firstMinuteCandle: null));

        Assert.That(await _candles.GetTradingStartAsync(Sber, Start.AddDays(-365), CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task An_incomplete_candle_or_one_outside_the_range_is_left_out()
    {
        _api.Respond(
            "GetCandles",
            TInvestAnswers.Candles(
                TInvestAnswers.Candle(Start.AddMinutes(-1), open: 300m, high: 300m, low: 300m, close: 300m, lots: 1),
                TInvestAnswers.Candle(Start, open: 300m, high: 300m, low: 300m, close: 300m, lots: 1),
                TInvestAnswers.Candle(Start.AddMinutes(1), open: 300m, high: 300m, low: 300m, close: 300m, lots: 1, complete: false)));

        IReadOnlyList<Candle> candles = await _candles.GetAsync(Sber, Day(), CancellationToken.None);

        Assert.That(candles.Select(candle => candle.OpenTime), Is.EqualTo(new[] { Start }));
    }

    [Test]
    public async Task Candles_come_oldest_first_with_the_volume_in_shares_and_no_turnover()
    {
        _api.Respond(
            "GetCandles",
            TInvestAnswers.Candles(
                TInvestAnswers.Candle(Start.AddMinutes(1), open: 300.5m, high: 301m, low: 300m, close: 300.75m, lots: 12),
                TInvestAnswers.Candle(Start, open: 300m, high: 300.5m, low: 299.95m, close: 300.5m, lots: 3)));

        IReadOnlyList<Candle> candles = await _candles.GetAsync(Sber, Day(), CancellationToken.None);

        Assert.That(
            candles,
            Is.EqualTo(
                new[]
                {
                    new Candle(Start, open: 300m, high: 300.5m, low: 299.95m, close: 300.5m, volume: 30m),
                    new Candle(Start.AddMinutes(1), open: 300.5m, high: 301m, low: 300m, close: 300.75m, volume: 120m)
                }));
    }

    [Test]
    public async Task The_call_asks_for_exchange_minute_candles_of_the_range_by_uid_with_the_token()
    {
        _api.Respond("GetCandles", TInvestAnswers.Candles());

        await _candles.GetAsync(Sber, Day(), CancellationToken.None);

        StubGrpcCall call = _api.Calls.Single();
        var request = (GetCandlesRequest)call.Request;
        Assert.Multiple(() =>
        {
            Assert.That(call.Method, Is.EqualTo("GetCandles"));
            Assert.That(request.InstrumentId, Is.EqualTo(SberUid));
            Assert.That(request.From.ToDateTimeOffset(), Is.EqualTo(Start));
            Assert.That(request.To.ToDateTimeOffset(), Is.EqualTo(Start.AddDays(1)));
            Assert.That(request.Interval, Is.EqualTo(CandleInterval._1Min));
            Assert.That(request.CandleSourceType, Is.EqualTo(GetCandlesRequest.Types.CandleSource.Exchange));
            Assert.That(call.Headers.GetValue("authorization"), Is.EqualTo($"Bearer {Token}"));
        });
    }

    [Test]
    public async Task The_trading_start_is_the_first_minute_candle_T_Invest_tells()
    {
        DateTimeOffset firstMinute = Start.AddDays(-4000);
        _api.Respond("ShareBy", TInvestAnswers.ShareBy(SberUid, firstMinute));

        DateTimeOffset? start = await _candles.GetTradingStartAsync(Sber, Start.AddDays(-365), CancellationToken.None);

        var request = (InstrumentRequest)_api.Calls.Single().Request;
        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(firstMinute));
            Assert.That(request.IdType, Is.EqualTo(InstrumentIdType.Uid));
            Assert.That(request.Id, Is.EqualTo(SberUid));
        });
    }

    [Test]
    public async Task Without_a_token_no_call_goes()
    {
        await _services.DisposeAsync();
        _candles = Candles(" ");

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<NotSupportedException>(() => _candles.GetAsync(Sber, Day(), CancellationToken.None));
            Assert.ThrowsAsync<NotSupportedException>(() => _candles.GetTradingStartAsync(Sber, Start, CancellationToken.None));
            Assert.That(_api.Calls, Is.Empty);
        });
    }
}