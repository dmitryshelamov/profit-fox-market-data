using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Instruments.Sync;
using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Mediatr.Behaviors;

[TestFixture]
public class PostgresTransactionBehaviorTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
    }

    [TearDown]
    public async Task TearDown()
    {
        _scope.Dispose();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;
    private IInstrumentRepository _repository = null!;

    private static async Task<IReadOnlyList<string>> StoredAsync()
    {
        await using ServiceProvider services = IntegrationEnvironment.Services();
        using IServiceScope scope = services.CreateScope();
        IReadOnlyList<Instrument> stored = await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().GetAllAsync(CancellationToken.None);

        return [.. stored.Select(instrument => instrument.Listing.Symbol)];
    }

    private static Instrument Linear(string baseAsset)
    {
        return InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear(baseAsset));
    }

    [Test]
    public async Task A_command_that_fails_saves_nothing()
    {
        IPipelineBehavior<SyncInstrumentsCommand, Unit> behavior =
            _scope.ServiceProvider.GetServices<IPipelineBehavior<SyncInstrumentsCommand, Unit>>().Single();

        Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new SyncInstrumentsCommand(BybitCategory.Linear, Now),
            async cancellationToken =>
            {
                await _repository.SaveAsync([Linear("BTC")], cancellationToken);
                await _repository.SaveAsync([Linear("ETH")], cancellationToken);

                throw new InvalidOperationException("The command fails after it saved.");
            },
            CancellationToken.None));

        Assert.That(await StoredAsync(), Is.Empty);
    }

    [Test]
    public async Task A_query_runs_without_the_transaction_of_a_command()
    {
        IPipelineBehavior<GetInstrumentsQuery, IReadOnlyCollection<InstrumentInternal>> behavior =
            _scope.ServiceProvider.GetServices<IPipelineBehavior<GetInstrumentsQuery, IReadOnlyCollection<InstrumentInternal>>>().Single();

        Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new GetInstrumentsQuery(),
            async cancellationToken =>
            {
                await _repository.SaveAsync([Linear("BTC")], cancellationToken);

                throw new InvalidOperationException("No transaction to roll back.");
            },
            CancellationToken.None));

        Assert.That(await StoredAsync(), Is.EqualTo(new[] { "BTCUSDT" }));
    }
}