using System.Net.Http.Json;
using System.Text.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.LongShortRatio.LoadLongShortRatio;
using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Application.Commands.LongShortRatio;

/// <summary>
///     The loading of the long/short ratio through the whole app: the history Bybit answers, the series in the database and the read
///     of the API.
/// </summary>
[TestFixture]
public class LoadLongShortRatioCommandHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _app = new MarketDataTestApplication();
        _client = _app.CreateClient();

        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Perpetual], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // Three minutes after 12:05, when Bybit has told the point of 12:05: the range ends at 12:10.
    private static readonly DateTimeOffset RequestedAt = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 8, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Until = RequestedAt.AddMinutes(2);

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static readonly Instrument Perpetual = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private MarketDataTestApplication _app = null!;
    private HttpClient _client = null!;

    private async Task<LoadLongShortRatioResult> LoadAsync(DateTimeOffset from, DateTimeOffset requestedAt)
    {
        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new LoadLongShortRatioCommand(Perpetual.Id, from, requestedAt), CancellationToken.None);
    }

    private async Task<LongShortRatioPointsInternal> ReadAsync()
    {
        LongShortRatioPointsInternal? points = await _client.GetFromJsonAsync<LongShortRatioPointsInternal>(
            $"api/instruments/{Perpetual.Id}/long-short-ratio?from=2026-11-14T00:00:00Z&to=2026-11-15T00:00:00Z",
            Json);

        return points!;
    }

    // The history of Bybit of the steps of [from, until), newest first, every share a half.
    private static string History(DateTimeOffset from, DateTimeOffset until)
    {
        return BybitAnswers.LongShortRatioHistory(
        [
            .. Enumerable.Range(start: 0, (int)((until - from).TotalMinutes / 5))
                .Select(step => from.AddMinutes(5 * step))
                .Reverse()
                .Select(time => BybitAnswers.LongShortRatioPoint("0.5", "0.5", time))
        ]);
    }

    [Test]
    public async Task A_round_after_the_history_is_loaded_asks_only_for_the_steps_told_since()
    {
        DateTimeOffset from = Until.AddMinutes(-60);
        _app.Bybit.RespondJson(History(from, Until)).RespondJson(History(Until, Until.AddHours(1)));
        await LoadAsync(from, RequestedAt);

        // An hour later, as the next round of the loading.
        LoadLongShortRatioResult result = await LoadAsync(from, RequestedAt.AddHours(1));
        LongShortRatioPointsInternal read = await ReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadLongShortRatioResult.NothingLeft));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(2));
            Assert.That(
                _app.Bybit.Requests[1].Query,
                Does.Contain($"startTime={Until.ToUnixTimeMilliseconds()}").And.Contain($"endTime={Until.AddHours(1).ToUnixTimeMilliseconds() - 1}"));

            // The twelve steps of the first hour and the twelve of the next.
            Assert.That(read.Points, Has.Count.EqualTo(24));
            Assert.That(read.CoveredFrom, Is.EqualTo(from));
            Assert.That(read.CoveredUntil, Is.EqualTo(Until.AddHours(1)));
        });
    }

    // While the history loads, a round of the market comes back to the contract in minutes, not in an hour: a page of one new step
    // each time would spend most requests of the loading on the newest points.
    [Test]
    public async Task A_round_minutes_later_while_the_history_is_left_asks_for_the_history()
    {
        DateTimeOffset from = Until.AddMinutes(-2600);
        _app.Bybit.RespondJson(History(Until.AddMinutes(-2500), Until)).RespondJson(History(from, Until.AddMinutes(-2500)));
        await LoadAsync(from, RequestedAt);

        // Five minutes later, when Bybit has told one more step.
        await LoadAsync(from, RequestedAt.AddMinutes(5));

        Assert.Multiple(() =>
        {
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(2));
            Assert.That(_app.Bybit.Requests[1].Query, Does.Contain($"endTime={Until.AddMinutes(-2500).ToUnixTimeMilliseconds() - 1}"));
        });
    }

    [Test]
    public async Task The_history_is_loaded_page_by_page_back_to_the_start_of_the_range()
    {
        // A page is 2500 minutes: the newest one first, then what is left down to the start, 2600 minutes back.
        DateTimeOffset from = Until.AddMinutes(-2600);
        _app.Bybit.RespondJson(History(Until.AddMinutes(-2500), Until)).RespondJson(History(from, Until.AddMinutes(-2500)));

        LoadLongShortRatioResult first = await LoadAsync(from, RequestedAt);
        LoadLongShortRatioResult second = await LoadAsync(from, RequestedAt);
        LoadLongShortRatioResult third = await LoadAsync(from, RequestedAt);
        LongShortRatioPointsInternal read = await ReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(LoadLongShortRatioResult.MoreLeft));
            Assert.That(second, Is.EqualTo(LoadLongShortRatioResult.NothingLeft));

            // A series that covers the range asks Bybit nothing more.
            Assert.That(third, Is.EqualTo(LoadLongShortRatioResult.NothingLeft));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(2));
            Assert.That(_app.Bybit.Requests[1].Query, Does.Contain($"endTime={Until.AddMinutes(-2500).ToUnixTimeMilliseconds() - 1}"));

            // The points of the day asked for: from midnight to 12:05, a step each.
            Assert.That(read.Points, Has.Count.EqualTo(146));
            Assert.That(read.CoveredFrom, Is.EqualTo(from));
            Assert.That(read.CoveredUntil, Is.EqualTo(Until));
        });
    }
}