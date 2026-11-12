using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Instruments;
using ProfitFox.MarketData.Application.Queries.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Instruments;

[TestFixture]
public class GetTInvestSharesQueryHandlerTests
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

    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;

    [Test]
    public async Task Returns_only_the_shares_of_tinvest()
    {
        Instrument linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());
        Instrument share = InstrumentGenerator.Generate(TInvestListedShareGenerator.Generate());
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([linear, share], CancellationToken.None);

        IReadOnlyCollection<InstrumentInternal> result =
            await _scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetTInvestSharesQuery(), CancellationToken.None);

        Assert.That(result.Select(instrument => instrument.Id), Is.EqualTo(new[] { share.Id }));
    }
}