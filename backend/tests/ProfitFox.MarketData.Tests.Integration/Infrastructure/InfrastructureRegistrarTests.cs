using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure;

[TestFixture]
public class InfrastructureRegistrarTests
{
    private static InstrumentSyncSettings SyncSettings(Dictionary<string, string?> values)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using ServiceProvider services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();

        return services.GetRequiredService<IOptions<InstrumentSyncSettings>>().Value;
    }

    [Test]
    public void A_sync_without_categories_is_refused()
    {
        Assert.Throws<OptionsValidationException>(() => SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "60"
            }));
    }

    [Test]
    public void An_interval_of_zero_is_refused()
    {
        Assert.Throws<OptionsValidationException>(() => SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "0",
                ["InstrumentSync:Categories:0"] = "Spot"
            }));
    }

    [Test]
    public void The_sync_takes_its_interval_and_categories_from_the_settings()
    {
        InstrumentSyncSettings settings = SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "60",
                ["InstrumentSync:Categories:0"] = "Spot",
                ["InstrumentSync:Categories:1"] = "Linear"
            });

        Assert.Multiple(() =>
        {
            Assert.That(settings.IntervalMinutes, Is.EqualTo(60));
            Assert.That(settings.Categories, Is.EqualTo(new[] { BybitCategory.Spot, BybitCategory.Linear }));
        });
    }
}