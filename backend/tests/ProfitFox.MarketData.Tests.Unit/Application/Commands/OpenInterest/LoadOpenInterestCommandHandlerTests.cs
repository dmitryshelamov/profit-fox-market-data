using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.OpenInterest.LoadOpenInterest;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.OpenInterest;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.OpenInterest;

[TestFixture]
public class LoadOpenInterestCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _instruments = Substitute.For<IInstrumentRepository>();
        _series = Substitute.For<IOpenInterestSeriesRepository>();
        _bybit = Substitute.For<IBybitOpenInterestClient>();
        _saved = null;

        // A page of four steps, so a test tells pages apart with a few points.
        _bybit.PageLength.Returns(Page);
        _instruments.GetAsync(Btc.Id, Arg.Any<CancellationToken>()).Returns(Btc);
        _series.SaveAsync(Arg.Do<OpenInterestSeries>(series => _saved = series), Arg.Any<CancellationToken>());

        _handler = new LoadOpenInterestCommandHandler(_instruments, _series, _bybit);
    }

    private static readonly TimeSpan Page = TimeSpan.FromMinutes(20);

    // Three minutes after 12:05, when Bybit has told the point of 12:05: the range ends at 12:10.
    private static readonly DateTimeOffset RequestedAt = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 8, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Until = RequestedAt.AddMinutes(2);

    private static readonly DateTimeOffset From = Until.AddDays(-365);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private IInstrumentRepository _instruments = null!;
    private IOpenInterestSeriesRepository _series = null!;
    private IBybitOpenInterestClient _bybit = null!;
    private OpenInterestSeries? _saved;
    private LoadOpenInterestCommandHandler _handler = null!;

    private Task<LoadOpenInterestResult> Load(Instrument? instrument = null, DateTimeOffset? from = null, DateTimeOffset? requestedAt = null)
    {
        return _handler.Handle(new LoadOpenInterestCommand((instrument ?? Btc).Id, from ?? From, requestedAt ?? RequestedAt), CancellationToken.None);
    }

    private void Stored(DateTimeOffset coveredFrom, DateTimeOffset coveredUntil)
    {
        _series.GetAsync(Btc.Id, Arg.Any<CancellationToken>()).Returns(OpenInterestSeries.Restore(Btc.Id, coveredFrom, coveredUntil));
    }

    private void StoredPoints(DateTimeOffset from, DateTimeOffset until, int count)
    {
        _series.CountPointsAsync(Btc.Id, from, until, Arg.Any<CancellationToken>()).Returns(count);
    }

    // Bybit has a point of every step of the range.
    private BybitOpenInterestPoint[] BybitHas(DateTimeOffset from, DateTimeOffset until)
    {
        BybitOpenInterestPoint[] points =
        [
            .. Enumerable.Range(start: 0, (int)((until - from) / OpenInterestSeries.Step))
                .Select(step => new BybitOpenInterestPoint(from + step * OpenInterestSeries.Step, 70000m + step))
        ];
        _bybit.GetAsync(Arg.Any<BybitListedInstrument>(), from, until, Arg.Any<CancellationToken>()).Returns(points);

        return points;
    }

    private Task BybitWasNotAsked()
    {
        return _bybit.DidNotReceiveWithAnyArgs().GetAsync(default!, from: default, until: default, cancellationToken: default);
    }

    [Test]
    public async Task A_contract_is_not_asked_for_the_time_before_it_was_launched()
    {
        // Launched twelve minutes ago, inside the step of 11:55: the range starts at that step, not a year back.
        Instrument young = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("NEW", launchTime: RequestedAt.AddMinutes(-12)));
        _instruments.GetAsync(young.Id, Arg.Any<CancellationToken>()).Returns(young);
        BybitHas(Until.AddMinutes(-15), Until);

        LoadOpenInterestResult result = await Load(young);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Until.AddMinutes(-15)));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
        });
    }

    [Test]
    public async Task A_contract_loaded_for_the_first_time_asks_for_the_newest_page_and_has_history_left()
    {
        BybitOpenInterestPoint[] told = BybitHas(Until - Page, Until);

        LoadOpenInterestResult result = await Load();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.MoreLeft));
            Assert.That(_saved!.InstrumentId, Is.EqualTo(Btc.Id));
            Assert.That(_saved.CoveredFrom, Is.EqualTo(Until - Page));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
            Assert.That(_saved.NewPoints, Is.EqualTo(told.Select(point => new OpenInterestPoint(Btc.Id, point.Time, point.Value))));
        });
    }

    [Test]
    public async Task A_page_after_the_coverage_whose_every_step_was_collected_is_covered_without_asking_Bybit()
    {
        // The collection kept 12:00 and 12:05 since the coverage ended.
        Stored(From, Until.AddMinutes(-10));
        StoredPoints(Until.AddMinutes(-10), Until, count: 2);

        LoadOpenInterestResult result = await Load();

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_saved!.CoveredUntil, Is.EqualTo(Until));
            Assert.That(_saved.NewPoints, Is.Empty);
            await BybitWasNotAsked();
        });
    }

    [Test]
    public async Task A_page_after_the_coverage_with_a_step_the_collection_missed_is_asked_from_Bybit()
    {
        Stored(From, Until.AddMinutes(-10));
        StoredPoints(Until.AddMinutes(-10), Until, count: 1);
        BybitOpenInterestPoint[] told = BybitHas(Until.AddMinutes(-10), Until);

        LoadOpenInterestResult result = await Load();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_saved!.CoveredUntil, Is.EqualTo(Until));
            Assert.That(_saved.NewPoints.Select(point => point.Time), Is.EqualTo(told.Select(point => point.Time)));
        });
    }

    [Test]
    public async Task A_request_told_in_another_time_zone_asks_for_the_same_page()
    {
        // The same moments, told at +03:00: the steps are counted in UTC.
        var moscow = TimeSpan.FromHours(3);
        BybitHas(Until - Page, Until);

        await Load(from: From.ToOffset(moscow), requestedAt: RequestedAt.ToOffset(moscow));

        Assert.Multiple(() =>
        {
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Until - Page));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
        });
    }

    [Test]
    public async Task A_series_covered_up_to_now_loads_the_newest_page_of_the_history_before_it()
    {
        Stored(Until.AddHours(-1), Until);
        BybitOpenInterestPoint[] told = BybitHas(Until.AddHours(-1) - Page, Until.AddHours(-1));

        LoadOpenInterestResult result = await Load();

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.MoreLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Until.AddHours(-1) - Page));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until));
            Assert.That(_saved.NewPoints, Has.Count.EqualTo(told.Length));

            // The history is asked whatever storage holds of it: only the time after the coverage is checked first.
            await _series.DidNotReceiveWithAnyArgs().CountPointsAsync(instrumentId: default, from: default, until: default, cancellationToken: default);
        });
    }

    [Test]
    public async Task A_series_that_covers_the_whole_range_asks_nothing()
    {
        Stored(From, Until);

        LoadOpenInterestResult result = await Load();

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_saved, Is.Null);
            await BybitWasNotAsked();
        });
    }

    [Test]
    public async Task A_spot_pair_has_no_open_interest_and_is_not_asked_for()
    {
        Instrument spot = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateSpot());
        _instruments.GetAsync(spot.Id, Arg.Any<CancellationToken>()).Returns(spot);

        LoadOpenInterestResult result = await Load(spot);

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_saved, Is.Null);
            await BybitWasNotAsked();
        });
    }

    [Test]
    public async Task A_step_Bybit_may_not_have_told_yet_is_left_for_later()
    {
        // A second before three minutes have passed since 12:05: the last step surely told is 12:00, so the range ends at 12:05.
        BybitHas(Until.AddMinutes(-5) - Page, Until.AddMinutes(-5));

        await Load(requestedAt: RequestedAt.AddSeconds(-1));

        Assert.That(_saved!.CoveredUntil, Is.EqualTo(Until.AddMinutes(-5)));
    }

    [Test]
    public async Task A_time_since_the_coverage_ended_longer_than_a_page_is_loaded_from_its_oldest_page()
    {
        // The app was down for an hour: the page next to the coverage comes first, so the coverage stays one stretch.
        Stored(From, Until.AddHours(-1));
        StoredPoints(Until.AddHours(-1), Until.AddHours(-1) + Page, count: 0);
        BybitHas(Until.AddHours(-1), Until.AddHours(-1) + Page);

        LoadOpenInterestResult result = await Load();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.MoreLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(From));
            Assert.That(_saved.CoveredUntil, Is.EqualTo(Until.AddHours(-1) + Page));
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
    public async Task The_last_page_of_the_history_ends_at_the_start_of_the_range_and_leaves_nothing()
    {
        // The range starts between two steps: its first step is the one after.
        DateTimeOffset from = Until.AddMinutes(-33);
        Stored(Until.AddMinutes(-20), Until);
        BybitHas(Until.AddMinutes(-30), Until.AddMinutes(-20));

        LoadOpenInterestResult result = await Load(from: from);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(LoadOpenInterestResult.NothingLeft));
            Assert.That(_saved!.CoveredFrom, Is.EqualTo(Until.AddMinutes(-30)));
        });
    }
}