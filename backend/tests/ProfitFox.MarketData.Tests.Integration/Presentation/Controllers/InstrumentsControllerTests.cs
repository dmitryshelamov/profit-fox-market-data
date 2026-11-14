using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Tests.Integration.Grpc;
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

    private const string SberUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

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
        HttpResponseMessage sync = await _client.PostAsync("api/instruments/bybit/sync?category=Inverse", content: null);

        Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task A_sync_of_tinvest_without_a_token_is_a_bad_request()
    {
        await using var app = new MarketDataTestApplication(tinvestToken: "");
        using HttpClient client = app.CreateClient();

        HttpResponseMessage sync = await client.PostAsync("api/instruments/tinvest/sync", content: null);

        Assert.Multiple(() =>
        {
            Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(app.TInvest.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task A_sync_while_Bybit_holds_the_requests_is_unavailable_and_tells_when_to_retry()
    {
        // An app of its own: the pause of Bybit stays in its gate. Bybit bans the IP, which pauses the requests for ten minutes.
        await using var app = new MarketDataTestApplication();
        using HttpClient client = app.CreateClient();
        app.Bybit.RespondJson("", HttpStatusCode.Forbidden);

        HttpResponseMessage sync = await client.PostAsync("api/instruments/bybit/sync?category=Linear", content: null);

        Assert.Multiple(() =>
        {
            Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(sync.Headers.RetryAfter?.Delta, Is.InRange(TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10)));
        });
    }

    [Test]
    public async Task A_synced_category_is_in_the_catalog_first_seen_at_the_request()
    {
        _app.Bybit.RespondJson(BybitAnswers.List("linear", items: BybitAnswers.LinearContract("BTCUSDT", "BTC")));

        DateTimeOffset before = DateTimeOffset.UtcNow;
        HttpResponseMessage sync = await _client.PostAsync("api/instruments/bybit/sync?category=Linear", content: null);
        DateTimeOffset after = DateTimeOffset.UtcNow;
        IReadOnlyList<InstrumentInternal>? catalog = await _client.GetFromJsonAsync<IReadOnlyList<InstrumentInternal>>("api/instruments", Json);

        Assert.Multiple(() =>
        {
            Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(
                catalog!.Select(instrument => instrument.Listing).Cast<BybitListedInstrumentInternal>().Select(listing => (listing.Category, listing.Symbol)),
                Is.EqualTo(new[] { (BybitCategory.Linear, "BTCUSDT") }));
            Assert.That(catalog!.Single().FirstSeenAt, Is.InRange(before, after));
            Assert.That(catalog!.Single().DelistedAt, Is.Null);
        });
    }

    [Test]
    public async Task A_synced_share_of_tinvest_is_in_the_catalog_with_its_uid()
    {
        _app.TInvest.Respond("Shares", TInvestAnswers.Shares(TInvestAnswers.Share("SBER", SberUid, lot: 10, priceStep: 0.01m)));

        HttpResponseMessage sync = await _client.PostAsync("api/instruments/tinvest/sync", content: null);
        IReadOnlyList<InstrumentInternal>? catalog = await _client.GetFromJsonAsync<IReadOnlyList<InstrumentInternal>>("api/instruments", Json);

        Assert.Multiple(() =>
        {
            Assert.That(sync.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(
                catalog!.Select(instrument => instrument.Listing),
                Is.EqualTo(
                    new[]
                    {
                        new TInvestListedShareInternal(
                            SberUid,
                            "SBER",
                            new InstrumentRulesInternal(TickSize: 0.01m, QuantityStep: 10m, MinQuantity: 10m, MinAmount: null))
                    }));
        });
    }

    [Test]
    public async Task Each_exchange_lists_only_its_own_instruments()
    {
        _app.Bybit.RespondJson(BybitAnswers.List("linear", items: BybitAnswers.LinearContract("BTCUSDT", "BTC")));
        _app.TInvest.Respond("Shares", TInvestAnswers.Shares(TInvestAnswers.Share("SBER", SberUid)));
        await _client.PostAsync("api/instruments/bybit/sync?category=Linear", content: null);
        await _client.PostAsync("api/instruments/tinvest/sync", content: null);

        IReadOnlyList<InstrumentInternal>? bybit = await _client.GetFromJsonAsync<IReadOnlyList<InstrumentInternal>>("api/instruments/bybit", Json);
        IReadOnlyList<InstrumentInternal>? tinvest = await _client.GetFromJsonAsync<IReadOnlyList<InstrumentInternal>>("api/instruments/tinvest", Json);

        Assert.Multiple(() =>
        {
            Assert.That(
                bybit!.Select(instrument => instrument.Listing).Cast<BybitListedInstrumentInternal>().Select(listing => listing.Symbol),
                Is.EqualTo(new[] { "BTCUSDT" }));
            Assert.That(
                tinvest!.Select(instrument => instrument.Listing).Cast<TInvestListedShareInternal>().Select(listing => listing.Uid),
                Is.EqualTo(new[] { SberUid }));
        });
    }
}