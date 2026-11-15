using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Application.Queries.Instruments;

[TestFixture]
public class GetInstrumentsQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateScope();
    }

    [TearDown]
    public async Task TearDown()
    {
        _scope.Dispose();
        await _services.DisposeAsync();
    }

    private static readonly Guid ListedId = Guid.Parse("0193a1f0-0000-7000-8000-000000000001");

    private static readonly Guid DelistedId = Guid.Parse("0193a1f0-0000-7000-8000-000000000002");

    private static readonly DateTimeOffset Then = new DateTimeOffset(year: 2026, month: 11, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LaunchTime = new DateTimeOffset(year: 2020, month: 3, day: 20, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;

    [TestCase(Exchange.Bybit)]
    [TestCase(Exchange.TInvest)]
    public async Task Returns_only_the_instruments_of_the_exchange_asked_for(Exchange exchange)
    {
        Instrument linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());
        Instrument share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([linear, share], CancellationToken.None);

        IReadOnlyCollection<InstrumentInternal> result =
            await _scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetInstrumentsQuery(exchange), CancellationToken.None);

        Assert.That(result.Select(instrument => instrument.Id), Is.EqualTo(new[] { exchange == Exchange.Bybit ? linear.Id : share.Id }));
    }

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
                BybitInstrumentStatus.Trading,
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
                BybitInstrumentStatus.Trading,
                new InstrumentRules(tickSize: 0.0001m, quantityStep: 0.01m, minQuantity: 0.01m)),
            Then);
        delisted.Delist(Now);
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([listed, delisted], CancellationToken.None);

        IReadOnlyCollection<InstrumentInternal> result =
            await _scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetInstrumentsQuery(), CancellationToken.None);

        Assert.That(
            result,
            Is.EqualTo(
                new[]
                {
                    new InstrumentInternal(
                        ListedId,
                        new BybitListedInstrumentInternal(
                            BybitCategory.Linear,
                            "BTCUSDT",
                            "BTC",
                            "USDT",
                            BybitInstrumentStatus.Trading,
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
                            BybitInstrumentStatus.Trading,
                            new InstrumentRulesInternal(TickSize: 0.0001m, QuantityStep: 0.01m, MinQuantity: 0.01m, MinAmount: null),
                            ContractType: null,
                            LaunchTime: null),
                        Then,
                        Now)
                }));
    }
}