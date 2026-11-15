using System.Net.Http.Json;
using System.Text.Json;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;
using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Application.Commands.Funding;

/// <summary>
///     The loading of funding payments through the whole app: the history Bybit answers, the series in the database and the read of
///     the API.
/// </summary>
[TestFixture]
public class LoadFundingPaymentsCommandHandlerTests
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

    // Half a minute after a whole hour, as the loading asks.
    private static readonly DateTimeOffset RequestedAt = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 16, minute: 0, second: 30, TimeSpan.Zero);

    private static readonly DateTimeOffset Hour = RequestedAt.AddSeconds(-30);

    private static readonly DateTimeOffset From = RequestedAt.AddDays(-365);

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static readonly Instrument Perpetual = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private MarketDataTestApplication _app = null!;
    private HttpClient _client = null!;

    private async Task<LoadFundingPaymentsResult> LoadAsync()
    {
        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new LoadFundingPaymentsCommand(Perpetual.Id, From, RequestedAt), CancellationToken.None);
    }

    private async Task<FundingPaymentsInternal> ReadAsync()
    {
        FundingPaymentsInternal? payments = await _client.GetFromJsonAsync<FundingPaymentsInternal>(
            $"api/instruments/{Perpetual.Id}/funding-payments?from=2025-11-01T00:00:00Z&to=2026-11-15T00:00:00Z",
            Json);

        return payments!;
    }

    // A page of Bybit: count payments eight hours apart, the newest paid at newest, written newest first.
    private static string Page(DateTimeOffset newest, int count)
    {
        return BybitAnswers.FundingPayments(
            [.. Enumerable.Range(start: 0, count).Select(index => BybitAnswers.FundingPayment("BTCUSDT", "0.0001", newest.AddHours(-8 * index)))]);
    }

    [Test]
    public async Task A_history_longer_than_a_page_is_loaded_page_by_page_back_to_the_start_of_the_range()
    {
        // The first page is full, so older payments may be left; the second is not, so it reaches the start of the range.
        _app.Bybit.RespondJson(Page(Hour, count: 200)).RespondJson(Page(Hour.AddHours(-8 * 200), count: 3));

        LoadFundingPaymentsResult first = await LoadAsync();
        FundingPaymentsInternal afterFirst = await ReadAsync();
        LoadFundingPaymentsResult second = await LoadAsync();
        FundingPaymentsInternal afterSecond = await ReadAsync();
        LoadFundingPaymentsResult third = await LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(LoadFundingPaymentsResult.MoreLeft));
            Assert.That(afterFirst.Points, Has.Count.EqualTo(200));
            Assert.That(afterFirst.CoveredFrom, Is.EqualTo(Hour.AddHours(-8 * 199)));
            Assert.That(afterFirst.CoveredUntil, Is.EqualTo(RequestedAt.AddSeconds(-15)));

            Assert.That(second, Is.EqualTo(LoadFundingPaymentsResult.NothingLeft));
            Assert.That(afterSecond.Points, Has.Count.EqualTo(203));
            Assert.That(afterSecond.Points[0].Time, Is.EqualTo(Hour.AddHours(-8 * 202)));
            Assert.That(afterSecond.Points[^1], Is.EqualTo(new FundingPaymentInternal(Hour, FundingRate: 0.0001m)));
            Assert.That(afterSecond.CoveredFrom, Is.EqualTo(From));

            // A series that covers the range asks Bybit nothing more.
            Assert.That(third, Is.EqualTo(LoadFundingPaymentsResult.NothingLeft));
            Assert.That(_app.Bybit.Requests, Has.Count.EqualTo(2));

            // The second page ends where the first one started.
            Assert.That(_app.Bybit.Requests[1].Query, Does.Contain($"endTime={Hour.AddHours(-8 * 199).ToUnixTimeMilliseconds() - 1}"));
        });
    }
}