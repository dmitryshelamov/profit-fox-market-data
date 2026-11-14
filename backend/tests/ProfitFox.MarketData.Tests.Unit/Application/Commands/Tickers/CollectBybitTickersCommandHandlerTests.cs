using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Tickers;
using ProfitFox.MarketData.Application.Ports.Transactions;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Tickers;

[TestFixture]
public class CollectBybitTickersCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _bybit = Substitute.For<IBybitTickersClient>();
        _instruments = Substitute.For<IInstrumentRepository>();
        _openInterest = Substitute.For<IOpenInterestRepository>();
        _fundingRates = Substitute.For<IFundingRateRepository>();
        _transactions = Substitute.For<ITransactionExecutor>();
        _savedOpenInterest = null;
        _savedFundingRates = null;
        _inTransaction = false;
        _savedOutsideTransaction = false;

        // As the executor does: the saves of the action are one, and it is over once the action is.
        _transactions.ExecuteAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (call[0] is Func<Task> action)
                {
                    _inTransaction = true;
                    await action();
                    _inTransaction = false;
                }
            });
        _openInterest.SaveAsync(
            Arg.Do<IReadOnlyCollection<OpenInterestPoint>>(points =>
            {
                _savedOpenInterest = points;
                _savedOutsideTransaction |= !_inTransaction;
            }),
            Arg.Any<CancellationToken>());
        _fundingRates.SaveAsync(
            Arg.Do<IReadOnlyCollection<FundingRatePoint>>(points =>
            {
                _savedFundingRates = points;
                _savedOutsideTransaction |= !_inTransaction;
            }),
            Arg.Any<CancellationToken>());

        _handler = new CollectBybitTickersCommandHandler(_bybit, _instruments, _openInterest, _fundingRates, _transactions);
    }

    private static readonly DateTimeOffset Boundary = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private IBybitTickersClient _bybit = null!;
    private IInstrumentRepository _instruments = null!;
    private IOpenInterestRepository _openInterest = null!;
    private IFundingRateRepository _fundingRates = null!;
    private ITransactionExecutor _transactions = null!;
    private IReadOnlyCollection<OpenInterestPoint>? _savedOpenInterest;
    private IReadOnlyCollection<FundingRatePoint>? _savedFundingRates;
    private bool _inTransaction;
    private bool _savedOutsideTransaction;
    private CollectBybitTickersCommandHandler _handler = null!;

    private Task<CollectBybitTickersResult> Collect()
    {
        return _handler.Handle(new CollectBybitTickersCommand(Boundary), CancellationToken.None);
    }

    private void Stored(params Instrument[] instruments)
    {
        // As the repository does: only the instruments of the exchange asked for.
        _instruments.GetByExchangeAsync(Arg.Any<Exchange>(), Arg.Any<CancellationToken>())
            .Returns(call => instruments.Where(instrument => instrument.Listing.Exchange == call.Arg<Exchange>()).ToList());
    }

    private void Tickers(params BybitLinearTicker[] tickers)
    {
        _bybit.GetLinearAsync(Arg.Any<CancellationToken>()).Returns(tickers);
    }

    [Test]
    public async Task A_contract_Bybit_gave_no_ticker_of_gets_no_points()
    {
        Stored(Btc, Eth);
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));

        CollectBybitTickersResult result = await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 1, FundingRatePoints: 1)));
            Assert.That(_savedOpenInterest!.Select(point => point.InstrumentId), Is.EqualTo(new[] { Btc.Id }));
            Assert.That(_savedFundingRates!.Select(point => point.InstrumentId), Is.EqualTo(new[] { Btc.Id }));
        });
    }

    [Test]
    public async Task A_contract_without_a_funding_rate_gets_only_its_open_interest()
    {
        Instrument futures = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("SOL"));
        Stored(Btc, futures);
        Tickers(
            new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m),
            new BybitLinearTicker("SOLUSDT", OpenInterest: 810.5m, FundingRate: null));

        CollectBybitTickersResult result = await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 2, FundingRatePoints: 1)));
            Assert.That(
                _savedOpenInterest,
                Is.EquivalentTo(new[] { new OpenInterestPoint(Btc.Id, Boundary, value: 52345.123m), new OpenInterestPoint(futures.Id, Boundary, value: 810.5m) }));
            Assert.That(_savedFundingRates, Is.EqualTo(new[] { new FundingRatePoint(Btc.Id, Boundary, rate: 0.0001m) }));
        });
    }

    [Test]
    public async Task A_contract_without_open_interest_gets_only_its_funding_rate()
    {
        Stored(Btc);
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: null, FundingRate: 0.0001m));

        CollectBybitTickersResult result = await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 0, FundingRatePoints: 1)));
            Assert.That(_savedOpenInterest, Is.Empty);
            Assert.That(_savedFundingRates, Is.EqualTo(new[] { new FundingRatePoint(Btc.Id, Boundary, rate: 0.0001m) }));
        });
    }

    [Test]
    public async Task A_delisted_contract_gets_no_points()
    {
        Instrument delisted = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("LUNA"));
        delisted.Delist(Boundary.AddDays(-1));
        Stored(delisted);
        Tickers(new BybitLinearTicker("LUNAUSDT", OpenInterest: 1m, FundingRate: 0.0001m));

        CollectBybitTickersResult result = await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 0, FundingRatePoints: 0)));
            Assert.That(_savedOpenInterest, Is.Null);
            Assert.That(_savedFundingRates, Is.Null);
        });
    }

    [Test]
    public async Task A_funding_rate_is_saved_as_Bybit_gave_it_below_zero_too()
    {
        Stored(Btc, Eth);
        Tickers(
            new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.00000331m),
            new BybitLinearTicker("ETHUSDT", OpenInterest: 1200000.45m, FundingRate: -0.00012m));

        await Collect();

        Assert.That(
            _savedFundingRates,
            Is.EquivalentTo(new[] { new FundingRatePoint(Btc.Id, Boundary, rate: 0.00000331m), new FundingRatePoint(Eth.Id, Boundary, rate: -0.00012m) }));
    }

    [Test]
    public async Task A_spot_pair_of_the_symbol_of_a_ticker_gets_no_points()
    {
        Instrument spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());
        Stored(spot, Btc);
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));

        await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(_savedOpenInterest!.Select(point => point.InstrumentId), Is.EqualTo(new[] { Btc.Id }));
            Assert.That(_savedFundingRates!.Select(point => point.InstrumentId), Is.EqualTo(new[] { Btc.Id }));
        });
    }

    [Test]
    public async Task A_symbol_the_catalog_does_not_have_is_left_out()
    {
        Stored(Btc);
        Tickers(
            new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m),
            new BybitLinearTicker("NEWUSDT", OpenInterest: 7m, FundingRate: 0.0002m));

        CollectBybitTickersResult result = await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 1, FundingRatePoints: 1)));
            Assert.That(_savedOpenInterest, Is.EqualTo(new[] { new OpenInterestPoint(Btc.Id, Boundary, value: 52345.123m) }));
            Assert.That(_savedFundingRates, Is.EqualTo(new[] { new FundingRatePoint(Btc.Id, Boundary, rate: 0.0001m) }));
        });
    }

    [Test]
    public void Bybit_that_holds_the_requests_fails_the_snapshot_and_nothing_is_saved()
    {
        Stored(Btc);
        _bybit.GetLinearAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangePausedException("Bybit limited the requests.", Boundary.AddSeconds(30)));

        Assert.Multiple(() =>
        {
            Assert.That(Collect, Throws.InstanceOf<ExchangePausedException>());
            Assert.That(_savedOpenInterest, Is.Null);
            Assert.That(_savedFundingRates, Is.Null);
        });
    }

    [Test]
    public async Task The_points_of_the_two_series_are_saved_in_one_transaction()
    {
        Stored(Btc);
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));

        await Collect();

        Assert.Multiple(async () =>
        {
            Assert.That(_savedOpenInterest, Is.Not.Null);
            Assert.That(_savedFundingRates, Is.Not.Null);
            Assert.That(_savedOutsideTransaction, Is.False);
            await _transactions.Received(1).ExecuteAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task When_the_catalog_has_no_contract_of_the_tickers_no_transaction_is_opened()
    {
        Stored();
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));

        CollectBybitTickersResult result = await Collect();

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 0, FundingRatePoints: 0)));
            await _transactions.DidNotReceive().ExecuteAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        });
    }
}