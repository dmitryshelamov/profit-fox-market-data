using NSubstitute;

using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Tests.Unit.Application.Queries.Liquidations;

[TestFixture]
public class GetLastLiquidationTimeQueryHandlerTests
{
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    [Test]
    public async Task The_latest_minute_storage_holds_is_told()
    {
        ILiquidationRepository liquidations = Substitute.For<ILiquidationRepository>();
        liquidations.GetLastTimeAsync(Arg.Any<CancellationToken>()).Returns(Minute);

        DateTimeOffset? last = await new GetLastLiquidationTimeQueryHandler(liquidations).Handle(new GetLastLiquidationTimeQuery(), CancellationToken.None);

        Assert.That(last, Is.EqualTo(Minute));
    }
}