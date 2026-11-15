using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;
using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Liquidations;

[TestFixture]
public class InvalidateLiquidationsCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _liquidations = Substitute.For<ILiquidationRepository>();
        _feed = Substitute.For<ILiveFeed<InstrumentLiquidationMinuteInternal>>();
        _marked = [];
        _told = [];
        _failsFrom = null;

        // As storage does: the first minute of the span of the first contract is the one the call changed.
        _liquidations.InvalidateAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                DateTimeOffset from = call.ArgAt<DateTimeOffset>(position: 1);

                if (from == _failsFrom)
                {
                    return Task.FromException<IReadOnlyList<StoredLiquidationMinute>>(new InvalidOperationException("Storage is down."));
                }

                _marked.Add((call.ArgAt<IReadOnlyCollection<Guid>>(position: 0), from, call.ArgAt<DateTimeOffset>(position: 2)));

                return Task.FromResult<IReadOnlyList<StoredLiquidationMinute>>(
                    [new StoredLiquidationMinute(new LiquidationMinute(Contracts[0], from, LiquidationSum.None, LiquidationSum.None), isValid: false)]);
            });
        _feed.Publish(Arg.Do<IReadOnlyCollection<InstrumentLiquidationMinuteInternal>>(minutes => _told.Add(minutes)));
        _handler = new InvalidateLiquidationsCommandHandler(_liquidations, _feed);
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid[] Contracts = [Guid.Parse("0193a1f0-0000-7000-8000-000000000001"), Guid.Parse("0193a1f0-0000-7000-8000-000000000002")];

    private static readonly LiquidationSumInternal Nothing = new LiquidationSumInternal(Volume: 0m, Turnover: 0m, Count: 0);

    private ILiquidationRepository _liquidations = null!;
    private ILiveFeed<InstrumentLiquidationMinuteInternal> _feed = null!;
    private List<(IReadOnlyCollection<Guid> InstrumentIds, DateTimeOffset From, DateTimeOffset Until)> _marked = null!;
    private List<IReadOnlyCollection<InstrumentLiquidationMinuteInternal>> _told = null!;
    private DateTimeOffset? _failsFrom;
    private InvalidateLiquidationsCommandHandler _handler = null!;

    private static InstrumentLiquidationMinuteInternal[] Told(DateTimeOffset time)
    {
        return [new InstrumentLiquidationMinuteInternal(Contracts[0], time, Nothing, Nothing, IsValid: false)];
    }

    [Test]
    public async Task A_span_of_more_than_an_hour_is_marked_an_hour_a_call_without_a_minute_left_out_or_marked_twice()
    {
        await _handler.Handle(new InvalidateLiquidationsCommand(Contracts, Minute, Minute.AddMinutes(150)), CancellationToken.None);

        Assert.That(
            _marked,
            Is.EqualTo(
                new (IReadOnlyCollection<Guid>, DateTimeOffset, DateTimeOffset)[]
                {
                    (Contracts, Minute, Minute.AddMinutes(59)),
                    (Contracts, Minute.AddMinutes(60), Minute.AddMinutes(119)),
                    (Contracts, Minute.AddMinutes(120), Minute.AddMinutes(150))
                }));
    }

    [Test]
    public void A_span_that_fails_leaves_the_ones_before_it_marked_and_told_and_stops_the_marking()
    {
        _failsFrom = Minute.AddMinutes(60);

        Assert.That(
            () => _handler.Handle(new InvalidateLiquidationsCommand(Contracts, Minute, Minute.AddMinutes(150)), CancellationToken.None),
            Throws.InvalidOperationException);
        Assert.Multiple(() =>
        {
            Assert.That(_marked.Select(span => span.From), Is.EqualTo(new[] { Minute }));
            Assert.That(_told, Is.EqualTo(new[] { Told(Minute) }));
        });
    }

    [Test]
    public async Task A_span_within_an_hour_is_marked_by_one_call_both_ends_included()
    {
        await _handler.Handle(new InvalidateLiquidationsCommand(Contracts, Minute, Minute.AddMinutes(2)), CancellationToken.None);

        Assert.That(_marked, Is.EqualTo(new (IReadOnlyCollection<Guid>, DateTimeOffset, DateTimeOffset)[] { (Contracts, Minute, Minute.AddMinutes(2)) }));
    }

    [Test]
    public async Task One_minute_is_marked_by_one_call()
    {
        await _handler.Handle(new InvalidateLiquidationsCommand(Contracts, Minute, Minute), CancellationToken.None);

        Assert.That(_marked, Is.EqualTo(new (IReadOnlyCollection<Guid>, DateTimeOffset, DateTimeOffset)[] { (Contracts, Minute, Minute) }));
    }

    [Test]
    public async Task The_minutes_a_span_changed_are_told_to_the_feed_as_storage_holds_them_a_span_at_a_time()
    {
        await _handler.Handle(new InvalidateLiquidationsCommand(Contracts, Minute, Minute.AddMinutes(90)), CancellationToken.None);

        Assert.That(_told, Is.EqualTo(new[] { Told(Minute), Told(Minute.AddMinutes(60)) }));
    }
}