using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Funding;

[TestFixture]
public class GetFundingPaymentsQueryHandlerTests
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

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 16, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private Task<FundingPaymentsInternal> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetFundingPaymentsQuery(instrumentId, new PointWindow(from, to)), CancellationToken.None);
    }

    // The series of an instrument covering [from, until) with the payments.
    private Task StoreAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset until, params FundingPayment[] payments)
    {
        return _scope.ServiceProvider.GetRequiredService<IFundingPaymentSeriesRepository>()
            .SaveAsync(FundingPaymentSeries.Create(instrument.Id, from, until, payments), CancellationToken.None);
    }

    // A payment an hour of the instrument back from the hour, each with its number as the rate.
    private Task StoreHoursAsync(int count)
    {
        return StoreAsync(
            Btc,
            Hour.AddHours(-count),
            Hour.AddHours(1),
            [.. Enumerable.Range(start: 0, count).Select(hour => new FundingPayment(Hour.AddHours(-hour), hour))]);
    }

    [Test]
    public async Task A_window_of_1000_payments_gives_them_all_and_nothing_to_continue_from()
    {
        await StoreHoursAsync(count: 1000);

        FundingPaymentsInternal payments = await ReadAsync(Btc.Id, Hour.AddDays(-365), Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(payments.Points, Has.Count.EqualTo(1000));
            Assert.That(payments.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task A_window_of_more_than_1000_payments_gives_the_oldest_1000_and_the_time_of_the_next()
    {
        await StoreHoursAsync(count: 1001);

        FundingPaymentsInternal payments = await ReadAsync(Btc.Id, Hour.AddDays(-365), Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(payments.Points, Has.Count.EqualTo(1000));
            Assert.That(payments.Points[0].Time, Is.EqualTo(Hour.AddHours(-1000)));
            Assert.That(payments.ContinueFrom, Is.EqualTo(Hour));
        });
    }

    [Test]
    public async Task An_instrument_of_the_catalog_with_nothing_loaded_gives_no_payments_and_no_coverage()
    {
        await StoreAsync(Btc, Hour.AddDays(-1), Hour, new FundingPayment(Hour.AddHours(-8), rate: 1m));

        FundingPaymentsInternal payments = await ReadAsync(Eth.Id, Hour.AddDays(-1), Hour);

        Assert.Multiple(() =>
        {
            Assert.That(payments.Points, Is.Empty);
            Assert.That(payments.CoveredFrom, Is.Null);
            Assert.That(payments.CoveredUntil, Is.Null);
            Assert.That(payments.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_refused()
    {
        Assert.That(() => ReadAsync(Guid.NewGuid(), Hour.AddDays(-1), Hour), Throws.InstanceOf<InstrumentNotFoundException>());
    }

    [Test]
    public async Task The_payments_of_the_window_come_oldest_first_with_the_coverage_of_the_whole_series()
    {
        await StoreAsync(
            Btc,
            Hour.AddDays(-30),
            Hour.AddSeconds(15),
            new FundingPayment(Hour.AddHours(-16), rate: 0.0001m),
            new FundingPayment(Hour.AddHours(-8), rate: -0.00118658m),
            new FundingPayment(Hour, rate: 0.00000819m));
        await StoreAsync(Eth, Hour.AddDays(-1), Hour, new FundingPayment(Hour.AddHours(-8), rate: 9m));

        // The window ends at the hour, so the payment of the hour is outside it.
        FundingPaymentsInternal payments = await ReadAsync(Btc.Id, Hour.AddHours(-16), Hour);

        Assert.Multiple(() =>
        {
            Assert.That(
                payments.Points,
                Is.EqualTo(
                    new[]
                    {
                        new FundingPaymentInternal(Hour.AddHours(-16), FundingRate: 0.0001m), new FundingPaymentInternal(Hour.AddHours(-8), FundingRate: -0.00118658m)
                    }));

            // The coverage is of the series, not of the window: it tells the window lies whole inside what is loaded.
            Assert.That(payments.CoveredFrom, Is.EqualTo(Hour.AddDays(-30)));
            Assert.That(payments.CoveredUntil, Is.EqualTo(Hour.AddSeconds(15)));
            Assert.That(payments.ContinueFrom, Is.Null);
        });
    }
}