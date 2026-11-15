using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.Controllers;

[TestFixture]
public class LongShortRatioControllerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app.Bybit.Requests.Clear();
        _services = IntegrationEnvironment.Services();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Linear, Spot, Share], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());

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

    [TestCase("from=2026-11-14T11:00:00Z&to=2026-11-14T10:00:00Z")]
    [TestCase("from=2026-11-14T10:00:00Z&to=2026-11-14T10:00:00Z")]
    [TestCase("from=2026-11-14T10:00:00Z")]
    [TestCase("to=2026-11-14T11:00:00Z")]
    [TestCase("from=yesterday&to=2026-11-14T11:00:00Z")]
    public async Task A_window_the_API_cannot_give_is_a_bad_request(string window)
    {
        HttpResponseMessage response = await _client.GetAsync($"api/instruments/{Linear.Id}/long-short-ratio?{window}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // A series of the linear contract that covers [from, until) with the points.
    private Task LoadAsync(DateTimeOffset from, DateTimeOffset until, params (DateTimeOffset Time, decimal Long)[] points)
    {
        return _services.GetRequiredService<ILongShortRatioSeriesRepository>()
            .SaveAsync(
                LongShortRatioSeries.Create(
                    Linear.Id,
                    from,
                    until,
                    [.. points.Select(point => new LongShortRatioPoint(Linear.Id, point.Time, point.Long, 1m - point.Long))]),
                CancellationToken.None);
    }

    private static string Url(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return $"api/instruments/{instrumentId}/long-short-ratio?from={from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&to={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
    }

    [Test]
    public async Task A_window_that_holds_more_than_an_answer_gives_tells_where_to_continue_from()
    {
        await LoadAsync(Hour, Hour.AddMinutes(5 * 1001), [.. Enumerable.Range(start: 0, count: 1001).Select(step => (Hour.AddMinutes(5 * step), step / 10000m))]);

        JsonElement first = await _client.GetFromJsonAsync<JsonElement>(Url(Linear.Id, Hour, Hour.AddDays(4)), Json);
        LongShortRatioPointsInternal? rest = await _client.GetFromJsonAsync<LongShortRatioPointsInternal>(
            Url(Linear.Id, first.GetProperty("continueFrom").GetDateTimeOffset(), Hour.AddDays(4)),
            Json);

        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("points").GetArrayLength(), Is.EqualTo(1000));
            Assert.That(first.GetProperty("continueFrom").GetDateTimeOffset(), Is.EqualTo(Hour.AddMinutes(5 * 1000)));
            Assert.That(rest!.Points, Is.EqualTo(new[] { new LongShortRatioPointInternal(Hour.AddMinutes(5 * 1000), Long: 0.1m, Short: 0.9m) }));
            Assert.That(rest.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task An_instrument_the_catalog_does_not_have_is_not_found()
    {
        HttpResponseMessage response = await _client.GetAsync(Url(Guid.NewGuid(), Hour, Hour.AddHours(1)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task An_instrument_without_a_long_short_ratio_gives_no_points()
    {
        // A spot pair and a share have none; the linear contract has none in the window.
        await LoadAsync(Hour.AddHours(2), Hour.AddHours(3), (Hour.AddHours(2), 0.7m));

        foreach (Instrument instrument in new[] { Spot, Share, Linear })
        {
            HttpResponseMessage response = await _client.GetAsync(Url(instrument.Id, Hour, Hour.AddHours(1)));
            LongShortRatioPointsInternal? points = await response.Content.ReadFromJsonAsync<LongShortRatioPointsInternal>(Json);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(points!.Points, Is.Empty);
                Assert.That(points.ContinueFrom, Is.Null);
            });
        }

        Assert.That(_app.Bybit.Requests, Is.Empty);
    }

    [Test]
    public async Task The_answer_tells_how_far_the_history_of_the_instrument_is_loaded()
    {
        await LoadAsync(Hour.AddDays(-30), Hour.AddMinutes(5), (Hour, 0.7302m));

        string loaded = await _client.GetStringAsync(Url(Linear.Id, Hour, Hour.AddHours(1)));
        string notLoaded = await _client.GetStringAsync(Url(Spot.Id, Hour, Hour.AddHours(1)));

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Does.Contain("\"coveredFrom\":\"2026-10-15T10:00:00+00:00\"").And.Contain("\"coveredUntil\":\"2026-11-14T10:05:00+00:00\""));
            Assert.That(loaded, Does.Contain("\"long\":0.7302"));
            Assert.That(notLoaded, Does.Contain("\"coveredFrom\":null").And.Contain("\"coveredUntil\":null"));
            Assert.That(_app.Bybit.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task The_stored_points_of_the_window_come_with_their_times_and_both_shares_without_asking_the_exchange()
    {
        // The steps between the points have none at the exchange; nothing is filled in for them.
        await LoadAsync(Hour, Hour.AddHours(2), (Hour, 0.7302m), (Hour.AddMinutes(35), 0.5m), (Hour.AddHours(1), 0.9m));

        HttpResponseMessage response = await _client.GetAsync(Url(Linear.Id, Hour, Hour.AddHours(1)));
        string body = await response.Content.ReadAsStringAsync();
        LongShortRatioPointsInternal? points = JsonSerializer.Deserialize<LongShortRatioPointsInternal>(body, Json);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                points!.Points,
                Is.EqualTo(
                    new[]
                    {
                        new LongShortRatioPointInternal(Hour, Long: 0.7302m, Short: 0.2698m),
                        new LongShortRatioPointInternal(Hour.AddMinutes(35), Long: 0.5m, Short: 0.5m)
                    }));
            Assert.That(points.ContinueFrom, Is.Null);

            // The names of the contract and the values exactly as stored.
            Assert.That(body, Does.Contain("\"long\":0.7302,\"short\":0.2698").And.Contain("\"time\":").And.Contain("\"continueFrom\":null"));
            Assert.That(_app.Bybit.Requests, Is.Empty);
        });
    }
}