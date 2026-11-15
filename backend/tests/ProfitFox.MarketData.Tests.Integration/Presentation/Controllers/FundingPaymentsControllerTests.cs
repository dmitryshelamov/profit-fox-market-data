using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.Controllers;

[TestFixture]
public class FundingPaymentsControllerTests
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

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 16, minute: 0, second: 0, TimeSpan.Zero);

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

    [TestCase("from=2026-11-14T17:00:00Z&to=2026-11-14T16:00:00Z")]
    [TestCase("from=2026-11-14T16:00:00Z&to=2026-11-14T16:00:00Z")]
    [TestCase("from=2026-11-14T16:00:00Z")]
    [TestCase("to=2026-11-14T17:00:00Z")]
    [TestCase("from=yesterday&to=2026-11-14T17:00:00Z")]
    public async Task A_window_the_API_cannot_give_is_a_bad_request(string window)
    {
        HttpResponseMessage response = await _client.GetAsync($"api/instruments/{Linear.Id}/funding-payments?{window}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // The series of the linear contract covering [from, until) with the payments.
    private async Task StoreAsync(DateTimeOffset from, DateTimeOffset until, params FundingPayment[] payments)
    {
        await _services.GetRequiredService<IFundingPaymentSeriesRepository>()
            .SaveAsync(FundingPaymentSeries.Create(Linear.Id, from, until, payments), CancellationToken.None);
    }

    private static string Url(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return $"api/instruments/{instrumentId}/funding-payments?from={from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&to={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
    }

    [Test]
    public async Task A_window_that_holds_more_than_an_answer_gives_tells_where_to_continue_from()
    {
        await StoreAsync(
            Hour.AddHours(-1001),
            Hour,
            [.. Enumerable.Range(start: 1, count: 1001).Select(hour => new FundingPayment(Hour.AddHours(-hour), hour))]);

        JsonElement first = await _client.GetFromJsonAsync<JsonElement>(Url(Linear.Id, Hour.AddDays(-365), Hour), Json);
        FundingPaymentsInternal? rest = await _client.GetFromJsonAsync<FundingPaymentsInternal>(
            Url(Linear.Id, first.GetProperty("continueFrom").GetDateTimeOffset(), Hour),
            Json);

        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("points").GetArrayLength(), Is.EqualTo(1000));
            Assert.That(first.GetProperty("continueFrom").GetDateTimeOffset(), Is.EqualTo(Hour.AddHours(-1)));
            Assert.That(rest!.Points, Is.EqualTo(new[] { new FundingPaymentInternal(Hour.AddHours(-1), FundingRate: 1m) }));
            Assert.That(rest.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task An_instrument_the_catalog_does_not_have_is_not_found()
    {
        HttpResponseMessage response = await _client.GetAsync(Url(Guid.NewGuid(), Hour.AddDays(-1), Hour));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task An_instrument_with_nothing_loaded_gives_no_payments_and_no_coverage()
    {
        // A spot pair and a share pay no funding; the payments of the linear contract are not loaded yet.
        foreach (Instrument instrument in new[] { Spot, Share, Linear })
        {
            HttpResponseMessage response = await _client.GetAsync(Url(instrument.Id, Hour.AddDays(-1), Hour));
            string body = await response.Content.ReadAsStringAsync();
            FundingPaymentsInternal? payments = JsonSerializer.Deserialize<FundingPaymentsInternal>(body, Json);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(payments!.Points, Is.Empty);
                Assert.That(body, Does.Contain("\"coveredFrom\":null").And.Contain("\"coveredUntil\":null"));
            });
        }

        Assert.That(_app.Bybit.Requests, Is.Empty);
    }

    [Test]
    public async Task The_stored_payments_of_the_window_come_with_how_far_they_are_loaded_without_asking_the_exchange()
    {
        await StoreAsync(
            Hour.AddDays(-30),
            Hour.AddSeconds(15),
            new FundingPayment(Hour.AddHours(-8), rate: -0.00118658m),
            new FundingPayment(Hour, rate: 0.00000819m));

        HttpResponseMessage response = await _client.GetAsync(Url(Linear.Id, Hour.AddDays(-1), Hour.AddHours(1)));
        string body = await response.Content.ReadAsStringAsync();
        FundingPaymentsInternal? payments = JsonSerializer.Deserialize<FundingPaymentsInternal>(body, Json);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                payments!.Points,
                Is.EqualTo(
                    new[] { new FundingPaymentInternal(Hour.AddHours(-8), FundingRate: -0.00118658m), new FundingPaymentInternal(Hour, FundingRate: 0.00000819m) }));
            Assert.That(payments.CoveredFrom, Is.EqualTo(Hour.AddDays(-30)));
            Assert.That(payments.CoveredUntil, Is.EqualTo(Hour.AddSeconds(15)));
            Assert.That(payments.ContinueFrom, Is.Null);

            // The names of the contract and the rate exactly as stored, not in the notation of a float.
            Assert.That(
                body,
                Does.Contain("\"fundingRate\":0.00000819").And.Contain("\"time\":").And.Contain("\"coveredFrom\":").And.Contain("\"continueFrom\":null"));
            Assert.That(_app.Bybit.Requests, Is.Empty);
        });
    }
}