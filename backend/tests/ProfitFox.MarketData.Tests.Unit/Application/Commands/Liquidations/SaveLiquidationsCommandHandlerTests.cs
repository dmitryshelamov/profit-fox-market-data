using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Liquidations;

[TestFixture]
public class SaveLiquidationsCommandHandlerTests
{
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Guid Btc = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    [Test]
    public async Task The_sums_are_added_to_the_stored_ones()
    {
        ILiquidationRepository liquidations = Substitute.For<ILiquidationRepository>();
        LiquidationMinute[] minutes = [LiquidationMinuteGenerator.Generate(Btc, Minute), LiquidationMinuteGenerator.Generate(Btc, Minute.AddMinutes(1))];

        await new SaveLiquidationsCommandHandler(liquidations).Handle(new SaveLiquidationsCommand(minutes), CancellationToken.None);

        await liquidations.Received(1).AddAsync(minutes, Arg.Any<CancellationToken>());
    }
}