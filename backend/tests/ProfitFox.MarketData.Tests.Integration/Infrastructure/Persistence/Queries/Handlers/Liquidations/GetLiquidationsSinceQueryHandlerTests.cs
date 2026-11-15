using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Liquidations;

[TestFixture]
public class GetLiquidationsSinceQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _liquidations = _scope.ServiceProvider.GetRequiredService<ILiquidationRepository>();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    // The ids put BTC before ETH among the minutes of one time.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private static readonly LiquidationSumInternal Nothing = new LiquidationSumInternal(Volume: 0m, Turnover: 0m, Count: 0);

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private ILiquidationRepository _liquidations = null!;

    private async Task<IReadOnlyList<InstrumentLiquidationMinuteInternal>> ReadAsync(DateTimeOffset since)
    {
        var read = new List<InstrumentLiquidationMinuteInternal>();

        ISender sender = _scope.ServiceProvider.GetRequiredService<ISender>();

        await foreach (InstrumentLiquidationMinuteInternal item in sender.CreateStream(new GetLiquidationsSinceQuery(since)))
        {
            read.Add(item);
        }

        return read;
    }

    [Test]
    public async Task Nothing_stored_since_the_time_gives_nothing()
    {
        await _liquidations.AddAsync([LiquidationMinuteGenerator.Generate(Btc.Id, Noon)], CancellationToken.None);

        Assert.That(await ReadAsync(Noon.AddMinutes(1)), Is.Empty);
    }

    [Test]
    public async Task The_minutes_of_every_instrument_at_or_after_the_time_come_oldest_first_exactly_as_stored_each_with_its_mark()
    {
        await _liquidations.AddAsync(
            [
                new LiquidationMinute(
                    Eth.Id,
                    Noon.AddMinutes(1),
                    new LiquidationSum(volume: 0.75m, turnover: 76125.5m, count: 3),
                    new LiquidationSum(volume: 0.001m, turnover: 101.337m, count: 1)),
                LiquidationMinuteGenerator.Generate(Btc.Id, Noon),
                LiquidationMinuteGenerator.Generate(Btc.Id, Noon.AddMinutes(-1))
            ],
            CancellationToken.None);
        await _liquidations.InvalidateAsync([Btc.Id], Noon.AddMinutes(1), Noon.AddMinutes(1), CancellationToken.None);

        IReadOnlyList<InstrumentLiquidationMinuteInternal> read = await ReadAsync(Noon);

        Assert.That(
            read,
            Is.EqualTo(
                new[]
                {
                    new InstrumentLiquidationMinuteInternal(Btc.Id, Noon, new LiquidationSumInternal(Volume: 2m, Turnover: 10m, Count: 1), Nothing, IsValid: true),
                    new InstrumentLiquidationMinuteInternal(Btc.Id, Noon.AddMinutes(1), Nothing, Nothing, IsValid: false),
                    new InstrumentLiquidationMinuteInternal(
                        Eth.Id,
                        Noon.AddMinutes(1),
                        new LiquidationSumInternal(Volume: 0.75m, Turnover: 76125.5m, Count: 3),
                        new LiquidationSumInternal(Volume: 0.001m, Turnover: 101.337m, Count: 1),
                        IsValid: true)
                }));
    }
}