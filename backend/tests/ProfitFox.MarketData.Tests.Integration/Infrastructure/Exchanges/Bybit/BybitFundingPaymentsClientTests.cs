using System.Net;

using Bybit.Net.Clients;
using Bybit.Net.Objects.Options;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;
using ProfitFox.MarketData.Tests.Integration.Http;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges.Bybit;

[TestFixture]
public class BybitFundingPaymentsClientTests
{
    [SetUp]
    public void SetUp()
    {
        _bybit = new StubHttpHandler();

        // The rate limiter of the SDK is off: it would wait in real time.
        _gate = new BybitRequestGate(
            new FixedOptionsMonitor<BybitApiSettings>(new BybitApiSettings()),
            new ExchangeRetryExecutor(TimeProvider.System),
            TimeProvider.System);
        _payments = new BybitFundingPaymentsClient(
            new BybitRestClient(
                new HttpClient(_bybit),
                NullLoggerFactory.Instance,
                Options.Create(new BybitRestOptions { RateLimiterEnabled = false })),
            _gate);
    }

    [TearDown]
    public void TearDown()
    {
        _bybit.Dispose();
        _gate.Dispose();
    }

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 16, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly BybitListedInstrument Btc = BybitListedInstrumentGenerator.GenerateLinear();

    private StubHttpHandler _bybit = null!;
    private BybitRequestGate _gate = null!;
    private BybitFundingPaymentsClient _payments = null!;

    [Test]
    public async Task A_contract_without_payments_in_the_range_gives_none()
    {
        _bybit.RespondJson(BybitAnswers.FundingPayments());

        IReadOnlyList<FundingPayment> payments = await _payments.GetAsync(Btc, Hour.AddDays(-1), Hour, CancellationToken.None);

        Assert.That(payments, Is.Empty);
    }

    [Test]
    public async Task A_payment_made_at_the_end_of_the_range_is_asked_out_and_left_out()
    {
        // Bybit takes both ends as included, so the request ends a millisecond earlier; a payment it gave at the end all the same is dropped.
        _bybit.RespondJson(
            BybitAnswers.FundingPayments(
                BybitAnswers.FundingPayment("BTCUSDT", "0.0002", Hour),
                BybitAnswers.FundingPayment("BTCUSDT", "0.0001", Hour.AddHours(-8))));

        IReadOnlyList<FundingPayment> payments = await _payments.GetAsync(Btc, Hour.AddHours(-8), Hour, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(payments, Is.EqualTo(new[] { new FundingPayment(Hour.AddHours(-8), rate: 0.0001m) }));
            Assert.That(
                _bybit.Requests.Single().Query,
                Does.Contain($"startTime={Hour.AddHours(-8).ToUnixTimeMilliseconds()}").And.Contain($"endTime={Hour.ToUnixTimeMilliseconds() - 1}"));
        });
    }

    [Test]
    public void An_answer_Bybit_refuses_gives_no_payments()
    {
        _bybit.RespondJson(BybitAnswers.Error(code: 10001, "params error: Symbol Invalid"));

        Assert.ThrowsAsync<HttpRequestException>(() => _payments.GetAsync(Btc, Hour.AddDays(-1), Hour, CancellationToken.None));
    }

    [Test]
    public void The_request_goes_through_the_gate_of_Bybit()
    {
        _bybit.RespondJson("", HttpStatusCode.Forbidden);

        Assert.Multiple(() =>
        {
            // The ban pauses the gate, and the pause holds the next request back.
            Assert.ThrowsAsync<ExchangePausedException>(() => _payments.GetAsync(Btc, Hour.AddDays(-1), Hour, CancellationToken.None));
            Assert.ThrowsAsync<ExchangePausedException>(() => _payments.GetAsync(Btc, Hour.AddDays(-1), Hour, CancellationToken.None));
            Assert.That(_bybit.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task The_payments_of_a_contract_come_in_one_request_of_a_full_page_oldest_first_exactly_as_Bybit_gave_them()
    {
        // Bybit gives the newest first.
        _bybit.RespondJson(
            BybitAnswers.FundingPayments(
                BybitAnswers.FundingPayment("BTCUSDT", "-0.00118658", Hour.AddHours(-8)),
                BybitAnswers.FundingPayment("BTCUSDT", "0.00000819", Hour.AddHours(-16))));

        IReadOnlyList<FundingPayment> payments = await _payments.GetAsync(Btc, Hour.AddDays(-1), Hour, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(
                payments,
                Is.EqualTo(new[] { new FundingPayment(Hour.AddHours(-16), rate: 0.00000819m), new FundingPayment(Hour.AddHours(-8), rate: -0.00118658m) }));
            Assert.That(_bybit.Requests.Single().AbsolutePath, Is.EqualTo("/v5/market/funding/history"));
            Assert.That(
                _bybit.Requests.Single().Query,
                Does.Contain("category=linear").And.Contain("symbol=BTCUSDT").And.Contain($"limit={_payments.PageSize}"));
            Assert.That(_payments.PageSize, Is.EqualTo(200));
        });
    }
}