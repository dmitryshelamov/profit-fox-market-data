using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Tests.Integration.Http;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.Controllers;

[TestFixture]
public class InstrumentsControllerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
    }

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private MarketDataTestApplication _app = null!;
    private HttpClient _client = null!;

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

    [Test]
    public async Task A_category_the_catalog_does_not_sync_is_a_bad_request()
    {
        HttpResponseMessage sync = await _client.PostAsync("api/instruments/sync?category=Inverse", content: null);

        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task A_synced_category_is_in_the_catalog_first_seen_at_the_request()
    {
        _app.Bybit.RespondJson(BybitAnswers.List("linear", items: BybitAnswers.LinearContract("BTCUSDT", "BTC")));

        DateTimeOffset before = DateTimeOffset.UtcNow;
        HttpResponseMessage sync = await _client.PostAsync("api/instruments/sync?category=Linear", content: null);
        DateTimeOffset after = DateTimeOffset.UtcNow;
        IReadOnlyList<InstrumentInternal>? catalog = await _client.GetFromJsonAsync<IReadOnlyList<InstrumentInternal>>("api/instruments", Json);

        Assert.Multiple(() =>
        {
            Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(catalog!.Select(instrument => (instrument.Listing.Category, instrument.Listing.Symbol)), Is.EqualTo(new[] { (BybitCategory.Linear, "BTCUSDT") }));
            Assert.That(catalog!.Single().FirstSeenAt, Is.InRange(before, after));
            Assert.That(catalog!.Single().DelistedAt, Is.Null);
        });
    }
}