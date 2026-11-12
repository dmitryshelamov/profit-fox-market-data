using NSubstitute;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Infrastructure.Persistence.Queries.Handlers.Instruments;

[TestFixture]
public class GetInstrumentsQueryHandlerTests
{
    private static readonly Guid ListedId = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly Guid DelistedId = Guid.Parse("0193a1f0-0000-7000-8000-000000000002");

    private static readonly DateTimeOffset Then = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LaunchTime = new DateTimeOffset(year: 2020, month: 3, day: 20, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public async Task Returns_every_instrument_of_the_catalog_as_a_contract()
    {
        var listed = Instrument.Create(
            ListedId,
            new BybitListedInstrument(
                BybitCategory.Linear,
                "BTCUSDT",
                "BTC",
                "USDT",
                InstrumentStatus.Trading,
                new InstrumentRules(tickSize: 0.1m, quantityStep: 0.001m, minQuantity: 0.001m, minAmount: 5m),
                BybitContractType.LinearPerpetual,
                LaunchTime),
            Then);
        var delisted = Instrument.Create(
            DelistedId,
            new BybitListedInstrument(
                BybitCategory.Spot,
                "LUNAUSDT",
                "LUNA",
                "USDT",
                InstrumentStatus.Trading,
                new InstrumentRules(tickSize: 0.0001m, quantityStep: 0.01m, minQuantity: 0.01m)),
            Then);
        delisted.Delist(Now);
        IInstrumentRepository repository = Substitute.For<IInstrumentRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([listed, delisted]);

        IReadOnlyCollection<InstrumentInternal> result =
            await new GetInstrumentsQueryHandler(repository).Handle(new GetInstrumentsQuery(), CancellationToken.None);

        Assert.That(result, Is.EqualTo(new[]
        {
            new InstrumentInternal(
                ListedId,
                new BybitListedInstrumentInternal(
                    BybitCategory.Linear,
                    "BTCUSDT",
                    "BTC",
                    "USDT",
                    InstrumentStatus.Trading,
                    new InstrumentRulesInternal(TickSize: 0.1m, QuantityStep: 0.001m, MinQuantity: 0.001m, MinAmount: 5m),
                    BybitContractType.LinearPerpetual,
                    LaunchTime),
                Then,
                DelistedAt: null),
            new InstrumentInternal(
                DelistedId,
                new BybitListedInstrumentInternal(
                    BybitCategory.Spot,
                    "LUNAUSDT",
                    "LUNA",
                    "USDT",
                    InstrumentStatus.Trading,
                    new InstrumentRulesInternal(TickSize: 0.0001m, QuantityStep: 0.01m, MinQuantity: 0.01m, MinAmount: null),
                    ContractType: null,
                    LaunchTime: null),
                Then,
                Now)
        }));
    }
}