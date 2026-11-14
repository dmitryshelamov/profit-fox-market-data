using System.Net.Http.Json;
using System.Text.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Application.Commands.Tickers;

/// <summary>
///     The collection of a snapshot through the whole app: the tickers Bybit answers, the catalog and the two series in the database, and
///     the reads of the API.
/// </summary>
[TestFixture]
public class CollectBybitTickersCommandHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app = new MarketDataTestApplication();
        _client = _app.CreateClient();

        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Perpetual, Futures, Spot], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static readonly DateTimeOffset Boundary = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static readonly Instrument Perpetual = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Futures = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private static readonly Instrument Spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());

    private MarketDataTestApplication _app = null!;
    private HttpClient _client = null!;

    private async Task<CollectBybitTickersResult> CollectAsync(DateTimeOffset boundary)
    {
        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CollectBybitTickersCommand(boundary), CancellationToken.None);
    }

    private async Task<IReadOnlyList<OpenInterestPointInternal>> OpenInterestAsync(Instrument instrument)
    {
        OpenInterestPointsInternal? points = await _client.GetFromJsonAsync<OpenInterestPointsInternal>(Url(instrument, "open-interest"), Json);

        return points!.Points;
    }

    private async Task<IReadOnlyList<FundingRatePointInternal>> FundingRateAsync(Instrument instrument)
    {
        FundingRatePointsInternal? points = await _client.GetFromJsonAsync<FundingRatePointsInternal>(Url(instrument, "funding-rate"), Json);

        return points!.Points;
    }

    // The day of the boundary.
    private static string Url(Instrument instrument, string series)
    {
        return $"api/instruments/{instrument.Id}/{series}?from=2026-11-14T00:00:00Z&to=2026-11-15T00:00:00Z";
    }

    [Test]
    public async Task A_snapshot_is_stored_as_the_points_of_its_boundary_and_read_back_by_instrument()
    {
        // ETHUSDT stands for a dated futures contract: its ticker has no funding rate. NEWUSDT is not in the catalog yet.
        _app.Bybit.RespondJson(
            BybitAnswers.Tickers(
                "linear",
                BybitAnswers.LinearTicker("BTCUSDT", "52345.123", "0.00000331"),
                BybitAnswers.LinearTicker("ETHUSDT", "810.5", fundingRate: ""),
                BybitAnswers.LinearTicker("NEWUSDT", "7", "0.0001")));

        CollectBybitTickersResult result = await CollectAsync(Boundary);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 2, FundingRatePoints: 1)));
            Assert.That(await OpenInterestAsync(Perpetual), Is.EqualTo(new[] { new OpenInterestPointInternal(Boundary, 52345.123m) }));
            Assert.That(await FundingRateAsync(Perpetual), Is.EqualTo(new[] { new FundingRatePointInternal(Boundary, 0.00000331m) }));
            Assert.That(await OpenInterestAsync(Futures), Is.EqualTo(new[] { new OpenInterestPointInternal(Boundary, 810.5m) }));
            Assert.That(await FundingRateAsync(Futures), Is.Empty);
            Assert.That(await OpenInterestAsync(Spot), Is.Empty);
            Assert.That(await FundingRateAsync(Spot), Is.Empty);
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Every_snapshot_adds_a_point_of_its_own_boundary()
    {
        _app.Bybit
            .RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT", "52345.123", "0.00000331")))
            .RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT", "52350", "-0.00000120")));

        await CollectAsync(Boundary);
        await CollectAsync(Boundary.AddMinutes(1));

        Assert.Multiple(async () =>
        {
            Assert.That(
                await OpenInterestAsync(Perpetual),
                Is.EqualTo(new[] { new OpenInterestPointInternal(Boundary, 52345.123m), new OpenInterestPointInternal(Boundary.AddMinutes(1), 52350m) }));
            Assert.That(
                await FundingRateAsync(Perpetual),
                Is.EqualTo(
                    new[] { new FundingRatePointInternal(Boundary, 0.00000331m), new FundingRatePointInternal(Boundary.AddMinutes(1), -0.00000120m) }));
        });
    }
}