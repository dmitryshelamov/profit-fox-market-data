using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Ports.Tickers;
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
        _openInterestFeed = Substitute.For<ILiveFeed<InstrumentOpenInterestInternal>>();
        _fundingRateFeed = Substitute.For<ILiveFeed<InstrumentFundingRateInternal>>();
        _savedOpenInterest = null;
        _savedFundingRates = null;
        _toldOpenInterest = null;
        _toldFundingRates = null;

        _openInterest.SaveAsync(Arg.Do<IReadOnlyCollection<OpenInterestPoint>>(points => _savedOpenInterest = points), Arg.Any<CancellationToken>());
        _fundingRates.SaveAsync(Arg.Do<IReadOnlyCollection<FundingRatePoint>>(points => _savedFundingRates = points), Arg.Any<CancellationToken>());
        _openInterestFeed.Publish(Arg.Do<IReadOnlyCollection<InstrumentOpenInterestInternal>>(points => _toldOpenInterest = points));
        _fundingRateFeed.Publish(Arg.Do<IReadOnlyCollection<InstrumentFundingRateInternal>>(points => _toldFundingRates = points));

        _handler = new CollectBybitTickersCommandHandler(_bybit, _instruments, _openInterest, _fundingRates, _openInterestFeed, _fundingRateFeed);
    }

    // A boundary where a step of the open interest series starts.
    private static readonly DateTimeOffset Boundary = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private IBybitTickersClient _bybit = null!;
    private IInstrumentRepository _instruments = null!;
    private IOpenInterestRepository _openInterest = null!;
    private IFundingRateRepository _fundingRates = null!;
    private ILiveFeed<InstrumentOpenInterestInternal> _openInterestFeed = null!;
    private ILiveFeed<InstrumentFundingRateInternal> _fundingRateFeed = null!;
    private IReadOnlyCollection<OpenInterestPoint>? _savedOpenInterest;
    private IReadOnlyCollection<FundingRatePoint>? _savedFundingRates;
    private IReadOnlyCollection<InstrumentOpenInterestInternal>? _toldOpenInterest;
    private IReadOnlyCollection<InstrumentFundingRateInternal>? _toldFundingRates;
    private CollectBybitTickersCommandHandler _handler = null!;

    private Task<CollectBybitTickersResult> Collect(DateTimeOffset? at = null)
    {
        return _handler.Handle(new CollectBybitTickersCommand(at ?? Boundary), CancellationToken.None);
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

    [TestCase(1)]
    [TestCase(4)]
    public void A_snapshot_of_a_time_between_two_steps_is_refused_before_Bybit_is_asked(int minutesAfterTheStep)
    {
        Stored(Btc);
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));

        Assert.Multiple(async () =>
        {
            Assert.That(() => Collect(Boundary.AddMinutes(minutesAfterTheStep)), Throws.ArgumentException);
            Assert.That(_savedFundingRates, Is.Null);
            await _bybit.DidNotReceive().GetLinearAsync(Arg.Any<CancellationToken>());
        });
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
            Assert.That(_savedOpenInterest, Is.Null);
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
            Assert.That(() => Collect(), Throws.InstanceOf<ExchangePausedException>());
            Assert.That(_savedOpenInterest, Is.Null);
            Assert.That(_savedFundingRates, Is.Null);
        });
    }

    [Test]
    public void Funding_rates_that_fail_to_save_leave_the_open_interest_of_the_step_saved()
    {
        Stored(Btc);
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));
        _fundingRates.SaveAsync(Arg.Any<IReadOnlyCollection<FundingRatePoint>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The database is away."));

        Assert.Multiple(() =>
        {
            Assert.That(() => Collect(), Throws.InvalidOperationException);
            Assert.That(_savedOpenInterest, Is.EqualTo(new[] { new OpenInterestPoint(Btc.Id, Boundary, value: 52345.123m) }));

            // The feed of each series tells what was saved of it, and nothing of what was not.
            Assert.That(_toldOpenInterest, Is.EqualTo(new[] { new InstrumentOpenInterestInternal(Btc.Id, Boundary, OpenInterest: 52345.123m) }));
            Assert.That(_toldFundingRates, Is.Null);
        });
    }

    [Test]
    public void Open_interest_that_fails_to_save_leaves_the_funding_rates_of_the_step_unsaved()
    {
        Stored(Btc);
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));
        _openInterest.SaveAsync(Arg.Any<IReadOnlyCollection<OpenInterestPoint>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The database is away."));

        Assert.Multiple(() =>
        {
            Assert.That(() => Collect(), Throws.InvalidOperationException);
            Assert.That(_savedFundingRates, Is.Null);
            Assert.That(_toldOpenInterest, Is.Null);
            Assert.That(_toldFundingRates, Is.Null);
        });
    }

    [Test]
    public async Task What_a_snapshot_saved_is_told_to_the_feed_of_its_series()
    {
        Instrument futures = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateFutures());
        Stored(Btc, futures);
        Tickers(
            new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: -0.00012m),
            new BybitLinearTicker("BTCUSDT-25DEC26", OpenInterest: 810.5m, FundingRate: null));

        await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(
                _toldOpenInterest,
                Is.EquivalentTo(
                    new[]
                    {
                        new InstrumentOpenInterestInternal(Btc.Id, Boundary, OpenInterest: 52345.123m),
                        new InstrumentOpenInterestInternal(futures.Id, Boundary, OpenInterest: 810.5m)
                    }));
            Assert.That(_toldFundingRates, Is.EqualTo(new[] { new InstrumentFundingRateInternal(Btc.Id, Boundary, FundingRate: -0.00012m) }));
        });
    }

    [Test]
    public async Task When_the_catalog_has_no_contract_of_the_tickers_nothing_is_saved()
    {
        Stored();
        Tickers(new BybitLinearTicker("BTCUSDT", OpenInterest: 52345.123m, FundingRate: 0.0001m));

        CollectBybitTickersResult result = await Collect();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new CollectBybitTickersResult(OpenInterestPoints: 0, FundingRatePoints: 0)));
            Assert.That(_savedOpenInterest, Is.Null);
            Assert.That(_savedFundingRates, Is.Null);
            Assert.That(_toldOpenInterest, Is.Null);
            Assert.That(_toldFundingRates, Is.Null);
        });
    }
}