using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;
using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Liquidations;

[TestFixture]
public class InvalidateLiquidationsCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _liquidations = Substitute.For<ILiquidationRepository>();
        _marked = [];
        _liquidations.InvalidateAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _marked.Add((call.ArgAt<IReadOnlyCollection<Guid>>(position: 0), call.ArgAt<DateTimeOffset>(position: 1), call.ArgAt<DateTimeOffset>(position: 2)));

                return Task.CompletedTask;
            });
        _handler = new InvalidateLiquidationsCommandHandler(_liquidations);
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid[] Contracts = [Guid.Parse("0193a1f0-0000-7000-8000-000000000001"), Guid.Parse("0193a1f0-0000-7000-8000-000000000002")];

    private ILiquidationRepository _liquidations = null!;
    private List<(IReadOnlyCollection<Guid> InstrumentIds, DateTimeOffset From, DateTimeOffset Until)> _marked = null!;
    private InvalidateLiquidationsCommandHandler _handler = null!;

    [Test]
    public async Task A_span_of_more_than_six_hours_is_marked_six_hours_a_call_without_a_minute_left_out_or_marked_twice()
    {
        await _handler.Handle(new InvalidateLiquidationsCommand(Contracts, Minute, Minute.AddHours(13)), CancellationToken.None);

        Assert.That(
            _marked,
            Is.EqualTo(
                new (IReadOnlyCollection<Guid>, DateTimeOffset, DateTimeOffset)[]
                {
                    (Contracts, Minute, Minute.AddHours(6).AddMinutes(-1)),
                    (Contracts, Minute.AddHours(6), Minute.AddHours(12).AddMinutes(-1)),
                    (Contracts, Minute.AddHours(12), Minute.AddHours(13))
                }));
    }

    [Test]
    public async Task A_span_within_six_hours_is_marked_by_one_call_both_ends_included()
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
}