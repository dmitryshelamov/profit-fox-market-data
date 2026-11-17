using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Funding;

/// <summary>
///     The coverage and the payments of the history of funding payments: the two reads of one call.
/// </summary>
[TestFixture]
public class GetFundingPaymentHistoryQueryHandlersTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 17, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Yesterday = Today.AddDays(-1);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    [Test]
    public async Task The_payments_inside_a_coverage_narrower_than_the_window_come_with_it()
    {
        // The coverage ends where the last page was loaded, a quarter of a minute after a whole hour.
        DateTimeOffset until = Yesterday.AddHours(16).AddSeconds(15);
        await _scope.ServiceProvider.GetRequiredService<IFundingPaymentSeriesRepository>()
            .SaveAsync(
                FundingPaymentSeries.Create(
                    Btc.Id,
                    Yesterday.AddDays(-365),
                    until,
                    [new FundingPayment(Yesterday.AddHours(8), -0.00118658m), new FundingPayment(Yesterday.AddHours(16), 0.00000819m)]),
                CancellationToken.None);
        ISender sender = _scope.ServiceProvider.GetRequiredService<ISender>();

        IReadOnlyList<InstrumentCoverageInternal> coverage =
            await sender.Send(new GetFundingPaymentHistoryCoverageQuery([Btc.Id], [], new HistoryWindow(Today.AddDays(-400), Today, Today)));
        var payments = new List<InstrumentFundingPaymentInternal>();

        await foreach (InstrumentFundingPaymentInternal payment in sender.CreateStream(new GetFundingPaymentHistoryQuery(coverage)))
        {
            payments.Add(payment);
        }

        Assert.Multiple(() =>
        {
            Assert.That(coverage.Single().Ranges, Is.EqualTo(new[] { new TimeRangeInternal(Yesterday.AddDays(-365), until) }));
            Assert.That(
                payments,
                Is.EqualTo(
                    new[]
                    {
                        new InstrumentFundingPaymentInternal(Btc.Id, Yesterday.AddHours(8), -0.00118658m),
                        new InstrumentFundingPaymentInternal(Btc.Id, Yesterday.AddHours(16), 0.00000819m)
                    }));
        });
    }
}