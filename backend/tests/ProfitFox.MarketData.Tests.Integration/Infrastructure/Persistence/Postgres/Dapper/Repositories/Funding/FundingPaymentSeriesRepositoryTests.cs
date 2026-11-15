using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding;

[TestFixture]
public class FundingPaymentSeriesRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IFundingPaymentSeriesRepository>();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 16, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private IFundingPaymentSeriesRepository _repository = null!;

    // The stored payments of an instrument, oldest first.
    private static async Task<IReadOnlyList<FundingPayment>> StoredAsync(Instrument instrument)
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        return
        [
            .. (await connection.QueryAsync<(DateTime Time, decimal Rate)>(
                "SELECT time, rate FROM funding_payments WHERE instrument_id = @Id ORDER BY time",
                new { instrument.Id }))
            .Select(row => new FundingPayment(new DateTimeOffset(row.Time, TimeSpan.Zero), row.Rate))
        ];
    }

    [Test]
    public async Task A_range_without_payments_is_saved_as_coverage_alone()
    {
        await _repository.SaveAsync(FundingPaymentSeries.Create(Btc.Id, Hour.AddDays(-365), Hour, []), CancellationToken.None);

        FundingPaymentSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(read!.CoveredFrom, Is.EqualTo(Hour.AddDays(-365)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Hour));
            Assert.That(await StoredAsync(Btc), Is.Empty);
        });
    }

    [Test]
    public async Task A_series_is_read_as_it_was_saved_with_no_payments_to_save_again()
    {
        FundingPayment[] payments = [new FundingPayment(Hour.AddHours(-8), rate: -0.00118658m), new FundingPayment(Hour, rate: 0.00000819m)];

        await _repository.SaveAsync(FundingPaymentSeries.Create(Btc.Id, Hour.AddHours(-8), Hour.AddSeconds(15), payments), CancellationToken.None);
        FundingPaymentSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(read!.InstrumentId, Is.EqualTo(Btc.Id));
            Assert.That(read.CoveredFrom, Is.EqualTo(Hour.AddHours(-8)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Hour.AddSeconds(15)));
            Assert.That(read.NewPayments, Is.Empty);

            // The rates exactly as the exchange gave them.
            Assert.That(await StoredAsync(Btc), Is.EqualTo(payments));
            Assert.That(await StoredAsync(Eth), Is.Empty);
        });
    }

    [Test]
    public async Task A_series_saved_again_takes_the_new_coverage_and_adds_its_new_payments()
    {
        await _repository.SaveAsync(
            FundingPaymentSeries.Create(Btc.Id, Hour.AddHours(-8), Hour, [new FundingPayment(Hour.AddHours(-8), rate: 1m)]),
            CancellationToken.None);
        FundingPaymentSeries series = (await _repository.GetAsync(Btc.Id, CancellationToken.None))!;

        // The payment of the hour comes with the next range; the one stored before is told again with another rate.
        series.Cover(Hour.AddDays(-1), Hour.AddHours(1), [new FundingPayment(Hour.AddHours(-8), rate: 2m), new FundingPayment(Hour, rate: 3m)]);
        await _repository.SaveAsync(series, CancellationToken.None);
        FundingPaymentSeries? read = await _repository.GetAsync(Btc.Id, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(read!.CoveredFrom, Is.EqualTo(Hour.AddDays(-1)));
            Assert.That(read.CoveredUntil, Is.EqualTo(Hour.AddHours(1)));
            Assert.That(
                await StoredAsync(Btc),
                Is.EqualTo(new[] { new FundingPayment(Hour.AddHours(-8), rate: 2m), new FundingPayment(Hour, rate: 3m) }));
        });
    }

    [Test]
    public async Task An_instrument_with_nothing_loaded_has_no_series()
    {
        await _repository.SaveAsync(FundingPaymentSeries.Create(Btc.Id, Hour.AddHours(-8), Hour, []), CancellationToken.None);

        Assert.That(await _repository.GetAsync(Eth.Id, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task Payments_that_cannot_be_stored_leave_the_coverage_unsaved_too()
    {
        // The catalog has no such instrument, so the rows of the series break their foreign keys.
        var unknown = FundingPaymentSeries.Create(Guid.NewGuid(), Hour.AddHours(-8), Hour, [new FundingPayment(Hour.AddHours(-8), rate: 1m)]);

        Assert.ThrowsAsync<PostgresException>(() => _repository.SaveAsync(unknown, CancellationToken.None));

        Assert.That(await _repository.GetAsync(unknown.InstrumentId, CancellationToken.None), Is.Null);
    }
}