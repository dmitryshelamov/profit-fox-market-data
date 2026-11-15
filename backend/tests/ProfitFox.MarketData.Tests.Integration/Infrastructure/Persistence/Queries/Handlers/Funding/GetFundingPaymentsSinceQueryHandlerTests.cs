using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Funding;

[TestFixture]
public class GetFundingPaymentsSinceQueryHandlerTests
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

    private async Task<IReadOnlyList<InstrumentFundingPaymentInternal>> ReadAsync(DateTimeOffset since)
    {
        var read = new List<InstrumentFundingPaymentInternal>();

        ISender sender = _scope.ServiceProvider.GetRequiredService<ISender>();

        await foreach (InstrumentFundingPaymentInternal item in sender.CreateStream(new GetFundingPaymentsSinceQuery(since)))
        {
            read.Add(item);
        }

        return read;
    }

    // The series of an instrument covering the day before noon and the hour after, with the payments.
    private Task StoreAsync(Instrument instrument, params FundingPayment[] payments)
    {
        return _scope.ServiceProvider.GetRequiredService<IFundingPaymentSeriesRepository>()
            .SaveAsync(FundingPaymentSeries.Create(instrument.Id, Noon.AddDays(-1), Noon.AddHours(1), payments), CancellationToken.None);
    }

    [Test]
    public async Task Nothing_paid_since_the_time_gives_nothing()
    {
        await StoreAsync(Btc, new FundingPayment(Noon.AddHours(-8), rate: 0.0001m));

        Assert.That(await ReadAsync(Noon), Is.Empty);
    }

    [Test]
    public async Task The_payments_of_every_instrument_at_or_after_the_time_come_oldest_first_exactly_as_stored()
    {
        await StoreAsync(Eth, new FundingPayment(Noon, rate: -0.00118658m));
        await StoreAsync(
            Btc,
            new FundingPayment(Noon.AddHours(-16), rate: 9m),
            new FundingPayment(Noon.AddHours(-8), rate: 0.00000819m),
            new FundingPayment(Noon, rate: 0.0001m));

        IReadOnlyList<InstrumentFundingPaymentInternal> read = await ReadAsync(Noon.AddHours(-8));

        Assert.That(
            read,
            Is.EqualTo(
                new[]
                {
                    new InstrumentFundingPaymentInternal(Btc.Id, Noon.AddHours(-8), FundingRate: 0.00000819m),
                    new InstrumentFundingPaymentInternal(Btc.Id, Noon, FundingRate: 0.0001m),
                    new InstrumentFundingPaymentInternal(Eth.Id, Noon, FundingRate: -0.00118658m)
                }));
    }
}