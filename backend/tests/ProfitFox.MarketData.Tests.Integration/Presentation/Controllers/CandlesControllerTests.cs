using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Presentation;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.Controllers;

[TestFixture]
public class CandlesControllerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app.Bybit.Requests.Clear();
        _services = IntegrationEnvironment.Services();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Linear, Share], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    // A past hour, so all its minutes are closed whatever the clock of the app says.
    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());

    private MarketDataTestApplication _app = null!;
    private HttpClient _client = null!;
    private ServiceProvider _services = null!;

    [OneTimeSetUp]
    public void StartApp()
    {
        _app = new MarketDataTestApplication();
        _client = _app.CreateClient();
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [TestCase("2h", "2026-11-02T10:00:00Z", "2026-11-02T12:00:00Z")]
    [TestCase("1h", "2026-11-02T12:00:00Z", "2026-11-02T10:00:00Z")]
    [TestCase("1h", "2026-11-02T10:30:00Z", "2026-11-02T12:00:00Z")]
    [TestCase("1d", "2026-11-02T00:00:00Z", "2026-11-03T03:00:00Z")]
    [TestCase("1m", "2026-11-02T00:00:00Z", "2026-11-02T16:41:00Z")]
    public async Task A_window_the_API_cannot_give_is_a_bad_request(string interval, string from, string to)
    {
        HttpResponseMessage response = await _client.GetAsync($"api/instruments/{Linear.Id}/candles?interval={interval}&from={from}&to={to}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task A_window_of_an_exchange_that_holds_the_requests_is_loading()
    {
        // An app of its own: the pause of Bybit stays in its gate.
        await using var app = new MarketDataTestApplication();
        using HttpClient client = app.CreateClient();
        app.Bybit.RespondJson("", HttpStatusCode.Forbidden);

        HttpResponseMessage response = await client.GetAsync(Url(Linear.Id, "1h", Hour, Hour.AddHours(1)));
        CandlesInternal? candles = await response.Content.ReadFromJsonAsync<CandlesInternal>(Json);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(candles!.Loading, Is.True);
            Assert.That(candles.Covered, Is.Empty);
        });
    }

    [Test]
    public async Task An_instrument_the_catalog_does_not_have_is_not_found()
    {
        HttpResponseMessage response = await _client.GetAsync(Url(Guid.NewGuid(), "1h", Hour, Hour.AddHours(1)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task A_stored_window_gives_the_candles_of_the_interval_without_asking_the_exchange()
    {
        await StoreAsync(
            Linear,
            new MinuteRange(Hour, Hour.AddHours(2)),
            new Candle(Hour, open: 5m, high: 6m, low: 4m, close: 5.5m, volume: 1m, turnover: 5m),
            new Candle(Hour.AddMinutes(1), open: 5.5m, high: 8m, low: 5m, close: 7m, volume: 2m, turnover: 14m),
            new Candle(Hour.AddHours(1), open: 7m, high: 7m, low: 7m, close: 7m, volume: 3m, turnover: 21m));

        CandlesInternal? candles = await _client.GetFromJsonAsync<CandlesInternal>(Url(Linear.Id, "1h", Hour, Hour.AddHours(2)), Json);

        Assert.Multiple(() =>
        {
            Assert.That(candles!.Loading, Is.False);
            Assert.That(candles.Candles, Is.EqualTo(new[]
            {
                new CandleInternal(Hour, Open: 5m, High: 8m, Low: 4m, Close: 7m, Volume: 3m, Turnover: 19m),
                new CandleInternal(Hour.AddHours(1), Open: 7m, High: 7m, Low: 7m, Close: 7m, Volume: 3m, Turnover: 21m)
            }));
            Assert.That(candles.Covered, Is.EqualTo(new[] { new MinuteRangeInternal(Hour, Hour.AddHours(2)) }));
            Assert.That(_app.Bybit.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task A_turnover_the_exchange_does_not_tell_is_estimated_minute_by_minute()
    {
        await StoreAsync(
            Share,
            new MinuteRange(Hour, Hour.AddHours(1)),
            new Candle(Hour, open: 2m, high: 3m, low: 1m, close: 2m, volume: 10m),
            new Candle(Hour.AddMinutes(1), open: 2m, high: 2m, low: 2m, close: 2m, volume: 5m));

        CandlesInternal? candles = await _client.GetFromJsonAsync<CandlesInternal>(Url(Share.Id, "1h", Hour, Hour.AddHours(1)), Json);

        Assert.That(candles!.Candles.Single().Turnover, Is.EqualTo(30m));
    }

    [Test]
    public async Task A_window_that_needs_more_requests_than_a_read_may_make_is_loading()
    {
        await using WebApplicationFactory<Program> app = _app.WithWebHostBuilder(builder => builder.UseSetting("CandleRead:MaxExchangeRequests", "1"));
        using HttpClient client = app.CreateClient();
        _app.Bybit.RespondJson(BybitAnswers.Klines("linear", "BTCUSDT"));

        // 400 candles of 5 minutes are 2000 minutes: two pages of Bybit.
        HttpResponseMessage response = await client.GetAsync(Url(Linear.Id, "5m", Hour, Hour.AddMinutes(2000)));
        CandlesInternal? candles = await response.Content.ReadFromJsonAsync<CandlesInternal>(Json);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(candles!.Loading, Is.True);
            Assert.That(candles.Candles, Is.Empty);
            Assert.That(candles.Covered, Is.EqualTo(new[] { new MinuteRangeInternal(Hour.AddMinutes(1000), Hour.AddMinutes(2000)) }));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Minutes_storage_does_not_hold_are_loaded_from_the_exchange_first()
    {
        _app.Bybit.RespondJson(BybitAnswers.Klines(
            "linear",
            "BTCUSDT",
            BybitAnswers.Kline(Hour.AddMinutes(1), "101", "102", "100", "101.5", "2", "203"),
            BybitAnswers.Kline(Hour, "100", "101", "99", "101", "1", "100")));

        CandlesInternal? candles = await _client.GetFromJsonAsync<CandlesInternal>(Url(Linear.Id, "1h", Hour, Hour.AddHours(1)), Json);

        Assert.Multiple(() =>
        {
            Assert.That(candles!.Candles, Is.EqualTo(new[] { new CandleInternal(Hour, Open: 100m, High: 102m, Low: 99m, Close: 101.5m, Volume: 3m, Turnover: 303m) }));
            Assert.That(candles.Covered, Is.EqualTo(new[] { new MinuteRangeInternal(Hour, Hour.AddHours(1)) }));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(1));
        });
    }

    private async Task StoreAsync(Instrument instrument, MinuteRange covered, params Candle[] candles)
    {
        var series = CandleSeries.Create(instrument.Id);
        series.Cover(covered, candles);
        await _services.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
    }

    private static string Url(Guid instrumentId, string interval, DateTimeOffset from, DateTimeOffset to)
    {
        return $"api/instruments/{instrumentId}/candles?interval={interval}&from={from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&to={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
    }
}