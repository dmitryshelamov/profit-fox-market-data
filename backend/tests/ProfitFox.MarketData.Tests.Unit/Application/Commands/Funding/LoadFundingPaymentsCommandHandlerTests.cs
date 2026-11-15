using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Funding;

[TestFixture]
public class LoadFundingPaymentsCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _instruments = Substitute.For<IInstrumentRepository>();
        _series = Substitute.For<IFundingPaymentSeriesRepository>();
        _bybit = Substitute.For<IBybitFundingPaymentsClient>();
        _saved = null;

        // A page of three payments, so a test tells a full page with a few of them.
        _bybit.PageSize.Returns(3);
        _instruments.GetAsync(Btc.Id, Arg.Any<CancellationToken>()).Returns(Btc);
        _series.SaveAsync(Arg.Do<FundingPaymentSeries>(series => _saved = series), Arg.Any<CancellationToken>());

        _handler = new LoadFundingPaymentsCommandHandler(_instruments, _series, _bybit);
    }

    // Half a minute after a whole hour, as the loading asks.
    private static readonly DateTimeOffset RequestedAt = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 16, minute: 0, second: 30, TimeSpan.Zero);

    private static readonly DateTimeOffset Hour = RequestedAt.AddSeconds(-30);

    // Payments made by now are known a quarter of a minute later.
    private static readonly DateTimeOffset Until = RequestedAt.AddSeconds(-15);

    private static readonly DateTimeOffset From = RequestedAt.AddDays(-365);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private IInstrumentRepository _instruments = null!;
    private IFundingPaymentSeriesRepository _series = null!;
    private IBybitFundingPaymentsClient _bybit = null!;
    private FundingPaymentSeries? _saved;
    private LoadFundingPaymentsCommandHandler _handler = null!;

    private Task<LoadFundingPaymentsResult> Load(Instrument? instrument = null)
    {
        return _handler.Handle(new LoadFundingPaymentsCommand((instrument ?? Btc).Id, From, RequestedAt), CancellationToken.None);
    }

    private static FundingPayment PaidAt(DateTimeOffset time)
    {
        return new FundingPayment(time, rate: 0.0001m);
    }

    private void Stored(DateTimeOffset coveredFrom, DateTimeOffset coveredUntil)
    {
        _series.GetAsync(Btc.Id, Arg.Any<CancellationToken>()).Returns(FundingPaymentSeries.Restore(Btc.Id, coveredFrom, coveredUntil));
    }

    private void BybitPaid(DateTimeOffset from, DateTimeOffset until, params FundingPayment[] payments)
    {
        _bybit.GetAsync(Arg.Any<BybitListedInstrument>(), from, until, Arg.Any<CancellationToken>()).Returns(payments);
    }

    private Task BybitWasNotAsked()
    {
        return _bybit.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default, default);
    }

    [Test]
    public async Task A_contract_loaded_for_the_first_time_asks_for_the_whole_range_and_covers_it_when_the_page_is_not_full()
    {
        FundingPayment[] payments = [PaidAt(Hour.AddHours(-8)), PaidAt(Hour)];
        BybitPaid(From, Until, payments);

        LoadFundingPaymentsResult result = await Load();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.NothingLeft));
            Assert.That(_saved!.InstrumentId, Is.EqualTo(Btc.Id));
            Assert.That(_saved.CoveredFrom, Is.EqualTo(From));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
            Assert.That(_saved.NewPayments, Is.EqualTo(payments));
        });
    }

    [Test]
    public async Task A_dated_futures_contract_pays_no_funding_and_is_not_asked_for()
    {
        Instrument futures = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateFutures());
        _instruments.GetAsync(futures.Id, Arg.Any<CancellationToken>()).Returns(futures);

        LoadFundingPaymentsResult result = await Load(futures);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.NothingLeft));
            Assert.That(_saved, Is.Null);
            await BybitWasNotAsked();
        });
    }

    [Test]
    public async Task A_full_page_covers_only_the_time_from_its_oldest_payment_and_leaves_more_to_load()
    {
        FundingPayment[] payments = [PaidAt(Hour.AddHours(-16)), PaidAt(Hour.AddHours(-8)), PaidAt(Hour)];
        BybitPaid(From, Until, payments);

        LoadFundingPaymentsResult result = await Load();

        Assert.Multiple(() =>
        {
            // Bybit gave its newest payments: older ones of the range may be left.
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.MoreLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Hour.AddHours(-16)));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
        });
    }

    [Test]
    public async Task A_series_covered_up_to_now_loads_the_history_before_it_the_newest_page_first()
    {
        Stored(Hour.AddDays(-30), Until);
        FundingPayment[] payments = [PaidAt(Hour.AddDays(-32)), PaidAt(Hour.AddDays(-31)), PaidAt(Hour.AddDays(-30).AddHours(-8))];
        BybitPaid(From, Hour.AddDays(-30), payments);

        LoadFundingPaymentsResult result = await Load();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.MoreLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Hour.AddDays(-32)));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
            Assert.That(_saved.NewPayments, Is.EqualTo(payments));
        });
    }

    [Test]
    public async Task A_series_that_covers_the_whole_range_asks_nothing()
    {
        Stored(From.AddDays(-1), Until);

        LoadFundingPaymentsResult result = await Load();

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.NothingLeft));
            Assert.That(_saved, Is.Null);
            await BybitWasNotAsked();
        });
    }

    [Test]
    public async Task A_spot_pair_pays_no_funding_and_is_not_asked_for()
    {
        Instrument spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());
        _instruments.GetAsync(spot.Id, Arg.Any<CancellationToken>()).Returns(spot);

        LoadFundingPaymentsResult result = await Load(spot);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.NothingLeft));
            await BybitWasNotAsked();
        });
    }

    [Test]
    public void An_instrument_of_another_exchange_is_refused()
    {
        Instrument share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());
        _instruments.GetAsync(share.Id, Arg.Any<CancellationToken>()).Returns(share);

        Assert.That(() => Load(share), Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_refused()
    {
        Instrument unknown = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

        Assert.That(() => Load(unknown), Throws.InstanceOf<InstrumentNotFoundException>());
    }

    [Test]
    public void Bybit_that_holds_the_requests_fails_the_page_and_nothing_is_saved()
    {
        _bybit.GetAsync(Arg.Any<BybitListedInstrument>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangePausedException("Bybit limited the requests.", RequestedAt.AddSeconds(30)));

        Assert.Multiple(() =>
        {
            Assert.That(() => Load(), Throws.InstanceOf<ExchangePausedException>());
            Assert.That(_saved, Is.Null);
        });
    }

    [Test]
    public async Task More_payments_than_a_page_since_the_coverage_ended_start_the_coverage_again_at_the_oldest_of_them()
    {
        Stored(From, Hour.AddDays(-10));
        FundingPayment[] payments = [PaidAt(Hour.AddHours(-16)), PaidAt(Hour.AddHours(-8)), PaidAt(Hour)];
        BybitPaid(Hour.AddDays(-10), Until, payments);

        LoadFundingPaymentsResult result = await Load();

        Assert.Multiple(() =>
        {
            // The payments between the old coverage and the page are unknown, so the history is loaded back from the page.
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.MoreLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Hour.AddHours(-16)));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
        });
    }

    [Test]
    public async Task The_last_page_of_the_history_covers_the_series_down_to_the_start_of_the_range()
    {
        Stored(Hour.AddDays(-30), Until);
        BybitPaid(From, Hour.AddDays(-30), PaidAt(Hour.AddDays(-31)));

        LoadFundingPaymentsResult result = await Load();

        Assert.Multiple(() =>
        {
            // A contract listed a month ago: the time before its first payment is covered with nothing in it.
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.NothingLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(From));
        });
    }

    [Test]
    public async Task What_was_paid_since_the_coverage_ended_is_loaded_before_the_history()
    {
        Stored(Hour.AddDays(-30), Hour.AddHours(-1).AddSeconds(15));
        BybitPaid(Hour.AddHours(-1).AddSeconds(15), Until, PaidAt(Hour));

        LoadFundingPaymentsResult result = await Load();

        Assert.Multiple(() =>
        {
            // The history before the coverage is still to load.
            Assert.That(result, Is.EqualTo(LoadFundingPaymentsResult.MoreLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Hour.AddDays(-30)));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
            Assert.That(_saved.NewPayments, Is.EqualTo(new[] { PaidAt(Hour) }));
        });
    }
}