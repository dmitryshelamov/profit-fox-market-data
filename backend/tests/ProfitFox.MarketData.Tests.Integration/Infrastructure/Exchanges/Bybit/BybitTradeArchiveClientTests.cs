using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Integration.Time;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitTradeArchiveClientTests
{
    [SetUp]
    public void SetUp()
    {
        _bybit = new StubHttpHandler();
        _time = new WatchedTimeProvider(Minute.AddDays(1));
        _http = new HttpClient(_bybit) { BaseAddress = new Uri("https://public.bybit.com/") };
        _archive = new BybitTradeArchiveClient(_http, new ExchangeRetryExecutor(_time));
    }

    [TearDown]
    public void TearDown()
    {
        _http.Dispose();
        _bybit.Dispose();
    }

    private const string LinearHeader = "timestamp,symbol,side,size,price,tickDirection,trdMatchID,grossValue,homeNotional,foreignNotional,RPI";

    private const string SpotHeader = "id,timestamp,price,volume,side,rpi";

    private static readonly DateOnly Day = new DateOnly(year: 2026, month: 11, day: 12);

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly BybitListedInstrument Linear = BybitListedInstrumentGenerator.GenerateLinear();

    private static readonly BybitListedInstrument Spot = BybitListedInstrumentGenerator.GenerateSpot();

    private StubHttpHandler _bybit = null!;
    private WatchedTimeProvider _time = null!;
    private HttpClient _http = null!;
    private BybitTradeArchiveClient _archive = null!;

    private static byte[] Gzip(params string[] lines)
    {
        using var packed = new MemoryStream();

        using (var gzip = new GZipStream(packed, CompressionMode.Compress))
        {
            gzip.Write(Encoding.UTF8.GetBytes(string.Join(separator: '\n', lines) + '\n'));
        }

        return packed.ToArray();
    }

    // A trade of a linear file: its time is in seconds with a fraction.
    private static string LinearTrade(DateTimeOffset time, string side, string size)
    {
        string seconds = (time.ToUnixTimeMilliseconds() / 1000m).ToString(CultureInfo.InvariantCulture);

        return $"{seconds},BTCUSDT,{side},{size},71435.00,ZeroMinusTick,aa81bc91,2.00018e+11,{size},2000.18,0";
    }

    // A trade of a spot file: its time is in milliseconds.
    private static string SpotTrade(DateTimeOffset time, string side, string volume)
    {
        return $"1,{time.ToUnixTimeMilliseconds()},71462.6,{volume},{side},0";
    }

    [Test]
    public async Task A_day_Bybit_has_no_archive_of_gives_nothing()
    {
        _bybit.RespondBytes([], HttpStatusCode.NotFound);

        IReadOnlyDictionary<DateTimeOffset, TradedVolume>? trades = await _archive.GetTradesAsync(Linear, Day, CancellationToken.None);

        Assert.That(trades, Is.Null);
    }

    [Test]
    public void A_file_laid_out_in_a_way_not_known_fails_without_being_read_again()
    {
        _bybit.RespondBytes(Gzip("time,qty,taker", "1,2,Buy"));

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<FormatException>(() => _archive.GetTradesAsync(Linear, Day, CancellationToken.None));
            Assert.That(_bybit.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task A_file_Bybit_refuses_for_now_is_read_again()
    {
        _bybit.RespondBytes([], HttpStatusCode.Forbidden).RespondBytes(Gzip(LinearHeader, LinearTrade(Minute, "Buy", "3")));

        Task<IReadOnlyDictionary<DateTimeOffset, TradedVolume>?> reading = _archive.GetTradesAsync(Linear, Day, CancellationToken.None);
        await _time.PassNextWaitAsync();

        Assert.Multiple(async () =>
        {
            Assert.That((await reading)![Minute], Is.EqualTo(new TradedVolume(traded: 3m, bought: 3m)));
            Assert.That(_bybit.Requests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task A_file_Bybit_keeps_refusing_is_a_transient_failure()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            _bybit.RespondBytes([], HttpStatusCode.ServiceUnavailable);
        }

        Task<IReadOnlyDictionary<DateTimeOffset, TradedVolume>?> reading = _archive.GetTradesAsync(Linear, Day, CancellationToken.None);

        for (var retry = 0; retry < 5; retry++)
        {
            await _time.PassNextWaitAsync();
        }

        Assert.ThrowsAsync<TransientExchangeException>(() => reading);
    }

    [Test]
    public async Task The_trades_of_a_linear_day_are_summed_by_their_minute()
    {
        _bybit.RespondBytes(
            Gzip(
                LinearHeader,
                LinearTrade(Minute.AddMilliseconds(37.5), "Buy", "2"),
                LinearTrade(Minute.AddSeconds(59.9), "Sell", "1.5"),
                LinearTrade(Minute.AddMinutes(1), "Sell", "4"),
                LinearTrade(Minute.AddMinutes(2), "Buy", "7")));

        IReadOnlyDictionary<DateTimeOffset, TradedVolume>? trades = await _archive.GetTradesAsync(Linear, Day, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                trades,
                Is.EqualTo(
                    new Dictionary<DateTimeOffset, TradedVolume>
                    {
                        [Minute] = new TradedVolume(traded: 3.5m, bought: 2m),
                        [Minute.AddMinutes(1)] = new TradedVolume(traded: 4m, bought: 0m),
                        [Minute.AddMinutes(2)] = new TradedVolume(traded: 7m, bought: 7m)
                    }));
            Assert.That(_bybit.Requests.Single().AbsolutePath, Is.EqualTo("/trading/BTCUSDT/BTCUSDT2026-11-12.csv.gz"));

            // Bybit refuses a request that names no client.
            Assert.That(_bybit.Clients.Single(), Is.Not.Empty);
        });
    }

    [Test]
    public async Task The_trades_of_a_spot_day_are_summed_by_their_minute()
    {
        _bybit.RespondBytes(Gzip(SpotHeader, SpotTrade(Minute, "buy", "0.25"), SpotTrade(Minute.AddSeconds(30), "sell", "0.5")));

        IReadOnlyDictionary<DateTimeOffset, TradedVolume>? trades = await _archive.GetTradesAsync(Spot, Day, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(trades, Is.EqualTo(new Dictionary<DateTimeOffset, TradedVolume> { [Minute] = new TradedVolume(traded: 0.75m, bought: 0.25m) }));
            Assert.That(_bybit.Requests.Single().AbsolutePath, Is.EqualTo("/spot/BTCUSDT/BTCUSDT_2026-11-12.csv.gz"));
        });
    }
}