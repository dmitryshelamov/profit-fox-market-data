using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using ProfitFox.MarketData.Infrastructure;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres;

[TestFixture]
public class PostgresHealthCheckTests
{
    [Test]
    public async Task A_database_that_answers_is_healthy()
    {
        await using ServiceProvider services = IntegrationEnvironment.Services();

        HealthReport report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.That(report.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    // Port 1 of the loopback has no server, so the connection is refused at once.
    [Test]
    public async Task A_database_that_does_not_answer_is_unhealthy()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:MarketData"] = "Host=127.0.0.1;Port=1;Database=marketdata;Username=marketdata;Password=marketdata" })
            .Build();
        await using ServiceProvider services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();

        HealthReport report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.That(report.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }
}