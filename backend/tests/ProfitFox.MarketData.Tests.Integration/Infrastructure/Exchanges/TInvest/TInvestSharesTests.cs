using Grpc.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Tests.Integration.Grpc;

using Tinkoff.InvestApi.V1;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.TInvest;

[TestFixture]
public class TInvestSharesTests
{
    [SetUp]
    public void SetUp()
    {
        _api = new StubGrpcInvoker();
        _shares = Shares(Token);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    private const string Token = "test-token";

    private const string AppName = "profit-fox-market-data";

    private const string SberUid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private StubGrpcInvoker _api = null!;
    private ServiceProvider _services = null!;
    private TInvestShares _shares = null!;

    // The settings as the app binds them from the TInvest section.
    private TInvestShares Shares(string token)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Token"] = token, ["AppName"] = AppName })
            .Build();
        _services = new ServiceCollection().Configure<TInvestApiSettings>(configuration).BuildServiceProvider();

        return new TInvestShares(
            new InstrumentsService.InstrumentsServiceClient(_api),
            _services.GetRequiredService<IOptionsMonitor<TInvestApiSettings>>(),
            TimeProvider.System);
    }

    [TestCase("SPBXM", "rub", true, 10, 0.01)]
    [TestCase("TQBR", "usd", true, 10, 0.01)]
    [TestCase("TQBR", "rub", false, 10, 0.01)]
    [TestCase("TQBR", "rub", true, 0, 0.01)]
    [TestCase("TQBR", "rub", true, 10, 0)]
    public async Task A_share_the_catalog_does_not_take_is_left_out(string classCode, string currency, bool apiTrade, int lot, decimal priceStep)
    {
        _api.Respond(
            "Shares",
            TInvestAnswers.Shares(
                TInvestAnswers.Share("SBER", SberUid),
                TInvestAnswers.Share("OTHER", "0a6b5bd4-5c3c-4cc2-a0a5-4c3e1e9d0a77", lot, priceStep, classCode, currency, apiTrade)));

        IReadOnlyList<TInvestListedShare> listed = await _shares.ListAsync(CancellationToken.None);

        Assert.That(listed.Select(share => share.Ticker), Is.EqualTo(new[] { "SBER" }));
    }

    [Test]
    public void A_failed_call_fails_the_whole_list()
    {
        _api.Fail("Shares", StatusCode.Unavailable);

        RpcException? failure = Assert.ThrowsAsync<RpcException>(() => _shares.ListAsync(CancellationToken.None));

        Assert.That(failure!.StatusCode, Is.EqualTo(StatusCode.Unavailable));
    }

    [Test]
    public async Task A_share_of_tqbr_in_roubles_is_listed_with_its_price_step_and_lot()
    {
        _api.Respond("Shares", TInvestAnswers.Shares(TInvestAnswers.Share("SBER", SberUid, lot: 10, priceStep: 0.01m)));

        IReadOnlyList<TInvestListedShare> listed = await _shares.ListAsync(CancellationToken.None);

        Assert.That(listed, Is.EqualTo(new[] { new TInvestListedShare(SberUid, "SBER", new InstrumentRules(tickSize: 0.01m, quantityStep: 10m, minQuantity: 10m)) }));
    }

    [Test]
    public async Task The_call_asks_for_the_base_status_with_the_token_and_the_name_of_the_app()
    {
        _api.Respond("Shares", TInvestAnswers.Shares());

        await _shares.ListAsync(CancellationToken.None);

        StubGrpcCall call = _api.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(call.Method, Is.EqualTo("Shares"));
            Assert.That(((InstrumentsRequest)call.Request).InstrumentStatus, Is.EqualTo(InstrumentStatus.Base));
            Assert.That(call.Headers.GetValue("authorization"), Is.EqualTo($"Bearer {Token}"));
            Assert.That(call.Headers.GetValue("x-app-name"), Is.EqualTo(AppName));
        });
    }

    [Test]
    public async Task Without_a_token_no_call_goes()
    {
        await _services.DisposeAsync();
        _shares = Shares(" ");

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<NotSupportedException>(() => _shares.ListAsync(CancellationToken.None));
            Assert.That(_api.Calls, Is.Empty);
        });
    }
}