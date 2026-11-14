using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Application.Ports.Transactions;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Funding;

[TestFixture]
public class FundingRateRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IFundingRateRepository>();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 5, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private IFundingRateRepository _repository = null!;

    // The stored points of every instrument, by instrument and time.
    private static async Task<IReadOnlyList<FundingRatePoint>> StoredAsync()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        return
        [
            .. (await connection.QueryAsync<(Guid InstrumentId, DateTime Time, decimal Rate)>(
                "SELECT instrument_id, time, rate FROM funding_rates ORDER BY instrument_id, time"))
            .Select(row => new FundingRatePoint(row.InstrumentId, new DateTimeOffset(row.Time, TimeSpan.Zero), row.Rate))
        ];
    }

    [Test]
    public async Task A_point_of_a_stored_instrument_and_time_takes_the_new_rate()
    {
        await _repository.SaveAsync(
            [new FundingRatePoint(Btc.Id, Minute, rate: 5m), new FundingRatePoint(Btc.Id, Minute.AddMinutes(1), rate: 6m)],
            CancellationToken.None);

        await _repository.SaveAsync([new FundingRatePoint(Btc.Id, Minute, rate: 7m)], CancellationToken.None);

        Assert.That(
            await StoredAsync(),
            Is.EqualTo(new[] { new FundingRatePoint(Btc.Id, Minute, rate: 7m), new FundingRatePoint(Btc.Id, Minute.AddMinutes(1), rate: 6m) }));
    }

    [Test]
    public async Task Points_saved_in_a_transaction_that_fails_are_not_stored()
    {
        ITransactionExecutor transactions = _scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        Assert.ThrowsAsync<InvalidOperationException>(() => transactions.ExecuteAsync(
            async () =>
            {
                await _repository.SaveAsync([new FundingRatePoint(Btc.Id, Minute, rate: 5m)], CancellationToken.None);

                throw new InvalidOperationException("The other series was not saved.");
            },
            CancellationToken.None));

        Assert.That(await StoredAsync(), Is.Empty);
    }

    [Test]
    public async Task Saving_no_points_stores_nothing()
    {
        await _repository.SaveAsync([], CancellationToken.None);

        Assert.That(await StoredAsync(), Is.Empty);
    }

    [Test]
    public async Task The_points_of_a_snapshot_are_stored_together_exactly_as_the_exchange_gave_them()
    {
        FundingRatePoint[] snapshot =
        [
            new FundingRatePoint(Btc.Id, Minute, rate: 0.00000331m),
            new FundingRatePoint(Eth.Id, Minute, rate: -0.00012m)
        ];

        await _repository.SaveAsync(snapshot, CancellationToken.None);

        Assert.That(await StoredAsync(), Is.EqualTo(snapshot));
    }
}