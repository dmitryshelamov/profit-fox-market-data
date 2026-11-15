using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;
using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Liquidations;

[TestFixture]
public class SaveLiquidationsCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _liquidations = Substitute.For<ILiquidationRepository>();
        _feed = Substitute.For<ILiveFeed<InstrumentLiquidationMinuteInternal>>();
        _told = null;
        _feed.Publish(Arg.Do<IReadOnlyCollection<InstrumentLiquidationMinuteInternal>>(minutes => _told = minutes));
        _handler = new SaveLiquidationsCommandHandler(_liquidations, _feed);
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private ILiquidationRepository _liquidations = null!;
    private ILiveFeed<InstrumentLiquidationMinuteInternal> _feed = null!;
    private IReadOnlyCollection<InstrumentLiquidationMinuteInternal>? _told;
    private SaveLiquidationsCommandHandler _handler = null!;

    [Test]
    public void A_save_that_fails_tells_the_feed_nothing()
    {
        _liquidations.AddAsync(Arg.Any<IReadOnlyCollection<LiquidationMinute>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Storage is down."));

        Assert.That(
            () => _handler.Handle(new SaveLiquidationsCommand([LiquidationMinuteGenerator.Generate(Btc, Minute)]), CancellationToken.None),
            Throws.InvalidOperationException);
        _feed.DidNotReceiveWithAnyArgs().Publish(default!);
    }

    [Test]
    public async Task The_sums_are_added_and_the_feed_is_told_the_minutes_as_storage_holds_them_not_as_they_were_added()
    {
        LiquidationMinute[] heard = [LiquidationMinuteGenerator.Generate(Btc, Minute), LiquidationMinuteGenerator.Generate(Btc, Minute.AddMinutes(1))];

        // Storage held sums of the first minute already, and that minute was not valid.
        var first = new LiquidationMinute(
            Btc,
            Minute,
            new LiquidationSum(volume: 5m, turnover: 40m, count: 3),
            new LiquidationSum(volume: 1m, turnover: 9m, count: 1));
        _liquidations.AddAsync(heard, Arg.Any<CancellationToken>())
            .Returns([new StoredLiquidationMinute(first, isValid: false), new StoredLiquidationMinute(heard[1], isValid: true)]);

        await _handler.Handle(new SaveLiquidationsCommand(heard), CancellationToken.None);

        Assert.That(
            _told,
            Is.EqualTo(
                new[]
                {
                    new InstrumentLiquidationMinuteInternal(
                        Btc,
                        Minute,
                        new LiquidationSumInternal(Volume: 5m, Turnover: 40m, Count: 3),
                        new LiquidationSumInternal(Volume: 1m, Turnover: 9m, Count: 1),
                        IsValid: false),
                    new InstrumentLiquidationMinuteInternal(
                        Btc,
                        Minute.AddMinutes(1),
                        new LiquidationSumInternal(Volume: 2m, Turnover: 10m, Count: 1),
                        new LiquidationSumInternal(Volume: 0m, Turnover: 0m, Count: 0),
                        IsValid: true)
                }));
    }
}