using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Funding;

[TestFixture]
public class GetFundingRatesSinceQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Noon = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    // The ids put BTC before ETH among the points of one time.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private async Task<IReadOnlyList<InstrumentFundingRateInternal>> ReadAsync(DateTimeOffset since)
    {
        var read = new List<InstrumentFundingRateInternal>();

        await foreach (InstrumentFundingRateInternal item in _scope.ServiceProvider.GetRequiredService<ISender>().CreateStream(new GetFundingRatesSinceQuery(since)))
        {
            read.Add(item);
        }

        return read;
    }

    private Task StoreAsync(params FundingRatePoint[] points)
    {
        return _scope.ServiceProvider.GetRequiredService<IFundingRateRepository>().SaveAsync(points, CancellationToken.None);
    }

    [Test]
    public async Task Nothing_stored_since_the_time_gives_nothing()
    {
        await StoreAsync(new FundingRatePoint(Btc.Id, Noon, rate: 0.0001m));

        Assert.That(await ReadAsync(Noon.AddMinutes(5)), Is.Empty);
    }

    [Test]
    public async Task The_points_of_every_instrument_at_or_after_the_time_come_oldest_first_exactly_as_stored()
    {
        await StoreAsync(
            new FundingRatePoint(Eth.Id, Noon.AddMinutes(5), rate: -0.00012m),
            new FundingRatePoint(Btc.Id, Noon.AddMinutes(5), rate: 0.0001m),
            new FundingRatePoint(Btc.Id, Noon, rate: 0.00000331m),
            new FundingRatePoint(Btc.Id, Noon.AddMinutes(-5), rate: 9m));

        IReadOnlyList<InstrumentFundingRateInternal> read = await ReadAsync(Noon);

        Assert.That(
            read,
            Is.EqualTo(
                new[]
                {
                    new InstrumentFundingRateInternal(Btc.Id, Noon, FundingRate: 0.00000331m),
                    new InstrumentFundingRateInternal(Btc.Id, Noon.AddMinutes(5), FundingRate: 0.0001m),
                    new InstrumentFundingRateInternal(Eth.Id, Noon.AddMinutes(5), FundingRate: -0.00012m)
                }));
    }
}