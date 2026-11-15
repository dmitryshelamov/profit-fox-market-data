using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.Controllers;

[TestFixture]
public class LiquidationsControllerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app.Bybit.Requests.Clear();
        _services = IntegrationEnvironment.Services();
        _liquidations = _services.GetRequiredService<ILiquidationRepository>();
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

    private static readonly LiquidationSumInternal Nothing = new LiquidationSumInternal(Volume: 0m, Turnover: 0m, Count: 0);

    private MarketDataTestApplication _app = null!;
    private HttpClient _client = null!;
    private ServiceProvider _services = null!;
    private ILiquidationRepository _liquidations = null!;

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
        HttpResponseMessage response = await _client.GetAsync($"api/instruments/{Linear.Id}/liquidations?{window}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static string Url(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return $"api/instruments/{instrumentId}/liquidations?from={from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&to={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}";
    }

    [Test]
    public async Task A_window_that_holds_more_than_an_answer_gives_tells_where_to_continue_from()
    {
        // A minute for each of 1001 minutes, none heard.
        await _liquidations.InvalidateAsync([Linear.Id], Hour, Hour.AddMinutes(1000), CancellationToken.None);

        JsonElement first = await _client.GetFromJsonAsync<JsonElement>(Url(Linear.Id, Hour, Hour.AddDays(1)), Json);
        LiquidationMinutesInternal? rest = await _client.GetFromJsonAsync<LiquidationMinutesInternal>(
            Url(Linear.Id, first.GetProperty("continueFrom").GetDateTimeOffset(), Hour.AddDays(1)),
            Json);

        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("minutes").GetArrayLength(), Is.EqualTo(1000));
            Assert.That(first.GetProperty("continueFrom").GetDateTimeOffset(), Is.EqualTo(Hour.AddMinutes(1000)));
            Assert.That(rest!.Minutes, Is.EqualTo(new[] { new LiquidationMinuteInternal(Hour.AddMinutes(1000), Nothing, Nothing, IsValid: false) }));
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
    public async Task An_instrument_without_liquidations_gives_no_minutes()
    {
        // A spot pair and a share have none; the linear contract has none heard in the window.
        await _liquidations.AddAsync([LiquidationMinuteGenerator.Generate(Linear.Id, Hour.AddHours(2))], CancellationToken.None);

        foreach (Instrument instrument in new[] { Spot, Share, Linear })
        {
            HttpResponseMessage response = await _client.GetAsync(Url(instrument.Id, Hour, Hour.AddHours(1)));
            LiquidationMinutesInternal? minutes = await response.Content.ReadFromJsonAsync<LiquidationMinutesInternal>(Json);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(minutes!.Minutes, Is.Empty);
                Assert.That(minutes.ContinueFrom, Is.Null);
            });
        }

        Assert.That(_app.Bybit.Requests, Is.Empty);
    }

    [Test]
    public async Task The_stored_minutes_of_the_window_come_with_their_sums_and_marks_without_asking_the_exchange()
    {
        // The minutes between the stored ones had no liquidation; nothing is filled in for them. The minute at 10:07 was not heard.
        var longs = new LiquidationSum(volume: 0.75m, turnover: 76125.5m, count: 3);
        var shorts = new LiquidationSum(volume: 0.00000331m, turnover: 0.337m, count: 1);
        await _liquidations.AddAsync(
            [new LiquidationMinute(Linear.Id, Hour, longs, shorts), LiquidationMinuteGenerator.Generate(Linear.Id, Hour.AddHours(1))],
            CancellationToken.None);
        await _liquidations.InvalidateAsync([Linear.Id], Hour.AddMinutes(7), Hour.AddMinutes(7), CancellationToken.None);

        HttpResponseMessage response = await _client.GetAsync(Url(Linear.Id, Hour, Hour.AddHours(1)));
        string body = await response.Content.ReadAsStringAsync();
        LiquidationMinutesInternal? minutes = JsonSerializer.Deserialize<LiquidationMinutesInternal>(body, Json);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                minutes!.Minutes,
                Is.EqualTo(
                    new[]
                    {
                        new LiquidationMinuteInternal(
                            Hour,
                            new LiquidationSumInternal(Volume: 0.75m, Turnover: 76125.5m, Count: 3),
                            new LiquidationSumInternal(Volume: 0.00000331m, Turnover: 0.337m, Count: 1),
                            IsValid: true),
                        new LiquidationMinuteInternal(Hour.AddMinutes(7), Nothing, Nothing, IsValid: false)
                    }));
            Assert.That(minutes.ContinueFrom, Is.Null);

            // The names of the contract and the values exactly as stored, not in the notation of a float.
            Assert.That(
                body,
                Does.Contain("\"longs\":{\"volume\":0.75,\"turnover\":76125.5,\"count\":3}")
                    .And.Contain("\"shorts\":{\"volume\":0.00000331,\"turnover\":0.337,\"count\":1}")
                    .And.Contain("\"isValid\":false")
                    .And.Contain("\"time\":")
                    .And.Contain("\"continueFrom\":null"));
            Assert.That(_app.Bybit.Requests, Is.Empty);
        });
    }
}