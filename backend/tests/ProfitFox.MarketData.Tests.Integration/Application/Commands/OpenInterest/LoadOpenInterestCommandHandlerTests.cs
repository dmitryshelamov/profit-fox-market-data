using System.Net.Http.Json;
using System.Text.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.OpenInterest.LoadOpenInterest;
using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Application.Commands.OpenInterest;

/// <summary>
///     The loading of open interest through the whole app: the history and the tickers Bybit answers, the series in the database and
///     the read of the API.
/// </summary>
[TestFixture]
public class LoadOpenInterestCommandHandlerTests
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

    private async Task<TResult> SendAsync<TResult>(IRequest<TResult> request)
    {
        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, CancellationToken.None);
    }

    private Task<LoadOpenInterestResult> LoadAsync(DateTimeOffset from, DateTimeOffset requestedAt)
    {
        return SendAsync(new LoadOpenInterestCommand(Perpetual.Id, from, requestedAt));
    }

    private async Task<OpenInterestPointsInternal> ReadAsync()
    {
        OpenInterestPointsInternal? points = await _client.GetFromJsonAsync<OpenInterestPointsInternal>(
            $"api/instruments/{Perpetual.Id}/open-interest?from=2026-11-14T00:00:00Z&to=2026-11-15T00:00:00Z",
            Json);

        return points!;
    }

    // The history of Bybit of the steps of [from, until), newest first, the value of a step the minute of the day it starts at.
    private static string History(DateTimeOffset from, DateTimeOffset until)
    {
        return BybitAnswers.OpenInterestHistory(
            "BTCUSDT",
            [
                .. Enumerable.Range(start: 0, (int)((until - from).TotalMinutes / 5))
                    .Select(step => from.AddMinutes(5 * step))
                    .Reverse()
                    .Select(time => BybitAnswers.OpenInterestPoint(time.TimeOfDay.TotalMinutes.ToString("0"), time))
            ]);
    }

    [Test]
    public async Task A_step_the_collection_missed_is_filled_from_the_history_and_a_collected_one_keeps_its_value()
    {
        // The history is loaded up to 12:10. Then the collection keeps 12:10, misses 12:15 and keeps 12:20.
        _app.Bybit.RespondJson(History(Until.AddMinutes(-10), Until));
        await LoadAsync(Until.AddMinutes(-10), RequestedAt);
        _app.Bybit
            .RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT", "38.16", "0.0001")))
            .RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT", "40", "0.0001")));
        await SendAsync(new CollectBybitTickersCommand(Until));
        await SendAsync(new CollectBybitTickersCommand(Until.AddMinutes(10)));

        // 12:28: the steps up to 12:25 are told. Bybit tells the collected ones again with other digits.
        _app.Bybit.RespondJson(History(Until, Until.AddMinutes(15)));
        LoadOpenInterestResult result = await LoadAsync(Until.AddMinutes(-10), RequestedAt.AddMinutes(15));
        OpenInterestPointsInternal read = await ReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(
                read.Points,
                Is.EqualTo(
                    new[]
                    {
                        new OpenInterestPointInternal(Until.AddMinutes(-10), OpenInterest: 720m),
                        new OpenInterestPointInternal(Until.AddMinutes(-5), OpenInterest: 725m),
                        new OpenInterestPointInternal(Until, OpenInterest: 38.16m),
                        new OpenInterestPointInternal(Until.AddMinutes(5), OpenInterest: 735m),
                        new OpenInterestPointInternal(Until.AddMinutes(10), OpenInterest: 40m)
                    }));
            Assert.That(read.CoveredFrom, Is.EqualTo(Until.AddMinutes(-10)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Until.AddMinutes(15)));
        });
    }

    [Test]
    public async Task Steps_the_collection_kept_are_covered_without_asking_Bybit_for_its_history()
    {
        _app.Bybit.RespondJson(History(Until.AddMinutes(-10), Until));
        await LoadAsync(Until.AddMinutes(-10), RequestedAt);
        _app.Bybit
            .RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT", "38.16", "0.0001")))
            .RespondJson(BybitAnswers.Tickers("linear", BybitAnswers.LinearTicker("BTCUSDT", "39", "0.0001")));
        await SendAsync(new CollectBybitTickersCommand(Until));
        await SendAsync(new CollectBybitTickersCommand(Until.AddMinutes(5)));
        int asked = _app.Bybit.Requests.Count;

        // 12:18: the steps 12:10 and 12:15 are told, and the collection has both.
        LoadOpenInterestResult result = await LoadAsync(Until.AddMinutes(-10), RequestedAt.AddMinutes(10));
        OpenInterestPointsInternal read = await ReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(asked));
            Assert.That(read.Points, Has.Count.EqualTo(4));
            Assert.That(read.CoveredUntil, Is.EqualTo(Until.AddMinutes(10)));
        });
    }

    [Test]
    public async Task The_history_is_loaded_page_by_page_back_to_the_start_of_the_range()
    {
        // A page is 1000 minutes: the newest one first, then what is left down to the start, 1100 minutes back.
        DateTimeOffset from = Until.AddMinutes(-1100);
        _app.Bybit.RespondJson(History(Until.AddMinutes(-1000), Until)).RespondJson(History(from, Until.AddMinutes(-1000)));

        LoadOpenInterestResult first = await LoadAsync(from, RequestedAt);
        LoadOpenInterestResult second = await LoadAsync(from, RequestedAt);
        LoadOpenInterestResult third = await LoadAsync(from, RequestedAt);
        OpenInterestPointsInternal read = await ReadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(LoadOpenInterestResult.MoreLeft));
            Assert.That(second, Is.EqualTo(LoadOpenInterestResult.NothingLeft));

            // A series that covers the range asks Bybit nothing more.
            Assert.That(third, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(2));
            Assert.That(_app.Bybit.Requests[1].Query, Does.Contain($"endTime={Until.AddMinutes(-1000).ToUnixTimeMilliseconds() - 1}"));

            // The points of the day asked for: from midnight to 12:05, a step each.
            Assert.That(read.Points, Has.Count.EqualTo(146));
            Assert.That(read.CoveredFrom, Is.EqualTo(from));
            Assert.That(read.CoveredUntil, Is.EqualTo(Until));
        });
    }
}