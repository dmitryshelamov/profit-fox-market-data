using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.Controllers;

[TestFixture]
public class FundingRateControllerTests
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
        HttpResponseMessage response = await _client.GetAsync($"api/instruments/{Linear.Id}/funding-rate?{window}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task A_window_that_holds_more_than_an_answer_gives_tells_where_to_continue_from()
    {
        await StoreAsync([.. Enumerable.Range(start: 0, count: 1001).Select(minute => new FundingRatePoint(Linear.Id, Hour.AddMinutes(minute), rate: minute))]);

        JsonElement first = await _client.GetFromJsonAsync<JsonElement>(Url(Linear.Id, Hour, Hour.AddDays(1)), Json);
        FundingRatePointsInternal? rest = await _client.GetFromJsonAsync<FundingRatePointsInternal>(
            Url(Linear.Id, first.GetProperty("continueFrom").GetDateTimeOffset(), Hour.AddDays(1)),
            Json);

        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("points").GetArrayLength(), Is.EqualTo(1000));
            Assert.That(first.GetProperty("continueFrom").GetDateTimeOffset(), Is.EqualTo(Hour.AddMinutes(1000)));
            Assert.That(rest!.Points, Is.EqualTo(new[] { new FundingRatePointInternal(Hour.AddMinutes(1000), 1000m) }));
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
    public async Task An_instrument_without_funding_gives_no_points()
    {
        // A spot pair and a share have none; the linear contract has none collected in the window.
        await StoreAsync(new FundingRatePoint(Linear.Id, Hour.AddHours(2), rate: 5m));

        foreach (Instrument instrument in new[] { Spot, Share, Linear })
        {
            HttpResponseMessage response = await _client.GetAsync(Url(instrument.Id, Hour, Hour.AddHours(1)));
            FundingRatePointsInternal? points = await response.Content.ReadFromJsonAsync<FundingRatePointsInternal>(Json);

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
    public async Task The_stored_points_of_the_window_come_with_their_times_without_asking_the_exchange()
    {
        // The minutes between the points were not collected; nothing is filled in for them.
        await StoreAsync(
            new FundingRatePoint(Linear.Id, Hour, rate: -0.00012m),
            new FundingRatePoint(Linear.Id, Hour.AddMinutes(7), rate: 0.00000331m),
            new FundingRatePoint(Linear.Id, Hour.AddHours(1), rate: 9m));

        HttpResponseMessage response = await _client.GetAsync(Url(Linear.Id, Hour, Hour.AddHours(1)));
        string body = await response.Content.ReadAsStringAsync();
        FundingRatePointsInternal? points = JsonSerializer.Deserialize<FundingRatePointsInternal>(body, Json);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                points!.Points,
                Is.EqualTo(new[] { new FundingRatePointInternal(Hour, -0.00012m), new FundingRatePointInternal(Hour.AddMinutes(7), 0.00000331m) }));
            Assert.That(points.ContinueFrom, Is.Null);

            // The names of the contract and the value exactly as stored, not in the notation of a float.
            Assert.That(body, Does.Contain("\"fundingRate\":0.00000331").And.Contain("\"time\":").And.Contain("\"continueFrom\":null"));
            Assert.That(_app.Bybit.Requests, Is.Empty);
        });
    }

    private async Task StoreAsync(params FundingRatePoint[] points)
    {
        await _services.GetRequiredService<IFundingRateRepository>().SaveAsync(points, CancellationToken.None);
    }

    private static string Url(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return $"api/instruments/{instrumentId}/funding-rate?from={from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&to={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
    }
}