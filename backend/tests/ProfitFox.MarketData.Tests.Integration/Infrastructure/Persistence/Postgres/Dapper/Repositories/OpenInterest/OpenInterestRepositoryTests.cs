using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Application.Ports.Transactions;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.OpenInterest;

[TestFixture]
public class OpenInterestRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IOpenInterestRepository>();
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
    private IOpenInterestRepository _repository = null!;

    // The stored points of every instrument, by instrument and time.
    private static async Task<IReadOnlyList<OpenInterestPoint>> StoredAsync()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        return
        [
            .. (await connection.QueryAsync<(Guid InstrumentId, DateTime Time, decimal Value)>(
                "SELECT instrument_id, time, value FROM open_interest ORDER BY instrument_id, time"))
            .Select(row => new OpenInterestPoint(row.InstrumentId, new DateTimeOffset(row.Time, TimeSpan.Zero), row.Value))
        ];
    }

    [Test]
    public async Task A_point_of_a_stored_instrument_and_time_takes_the_new_value()
    {
        await _repository.SaveAsync(
            [new OpenInterestPoint(Btc.Id, Minute, value: 5m), new OpenInterestPoint(Btc.Id, Minute.AddMinutes(1), value: 6m)],
            CancellationToken.None);

        await _repository.SaveAsync([new OpenInterestPoint(Btc.Id, Minute, value: 7m)], CancellationToken.None);

        Assert.That(
            await StoredAsync(),
            Is.EqualTo(new[] { new OpenInterestPoint(Btc.Id, Minute, value: 7m), new OpenInterestPoint(Btc.Id, Minute.AddMinutes(1), value: 6m) }));
    }

    [Test]
    public async Task Points_saved_in_a_transaction_that_fails_are_not_stored()
    {
        ITransactionExecutor transactions = _scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        Assert.ThrowsAsync<InvalidOperationException>(() => transactions.ExecuteAsync(
            async () =>
            {
                await _repository.SaveAsync([new OpenInterestPoint(Btc.Id, Minute, value: 5m)], CancellationToken.None);

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
        OpenInterestPoint[] snapshot =
        [
            new OpenInterestPoint(Btc.Id, Minute, value: 52345.123m),
            new OpenInterestPoint(Eth.Id, Minute, value: 1234567890.00000001m)
        ];

        await _repository.SaveAsync(snapshot, CancellationToken.None);

        Assert.That(await StoredAsync(), Is.EqualTo(snapshot));
    }
}