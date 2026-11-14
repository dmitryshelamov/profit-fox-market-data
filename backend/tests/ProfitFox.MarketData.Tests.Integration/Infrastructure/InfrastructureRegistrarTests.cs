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
    public void A_stream_of_shares_without_a_token_is_refused()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CandleStream:TInvest:Enabled"] = "true" })
            .Build();
        using ServiceProvider services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => _ = services.GetRequiredService<IOptions<CandleStreamSettings>>().Value);
    }

    [Test]
    public void A_sync_of_bybit_without_categories_is_refused()
    {
        Assert.Throws<OptionsValidationException>(() => SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "60",
                ["InstrumentSync:Bybit:Enabled"] = "true"
            }));
    }

    [Test]
    public void A_sync_of_nothing_is_refused()
    {
        Assert.Throws<OptionsValidationException>(() => SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "60",
                ["InstrumentSync:Bybit:Categories:0"] = "Spot"
            }));
    }

    [Test]
    public void A_sync_of_shares_alone_takes_the_token()
    {
        InstrumentSyncSettings settings = SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "60",
                ["InstrumentSync:TInvest:Enabled"] = "true",
                ["TInvestApi:Token"] = "test-token"
            });

        Assert.Multiple(() =>
        {
            Assert.That(settings.TInvest.Enabled, Is.True);
            Assert.That(settings.Bybit.Enabled, Is.False);
        });
    }

    [Test]
    public void A_sync_of_shares_without_a_token_is_refused()
    {
        Assert.Throws<OptionsValidationException>(() => SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "60",
                ["InstrumentSync:TInvest:Enabled"] = "true"
            }));
    }

    [Test]
    public void An_interval_of_zero_is_refused()
    {
        Assert.Throws<OptionsValidationException>(() => SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "0",
                ["InstrumentSync:Bybit:Enabled"] = "true",
                ["InstrumentSync:Bybit:Categories:0"] = "Spot"
            }));
    }

    [Test]
    public void The_sync_takes_its_interval_and_exchanges_from_the_settings()
    {
        InstrumentSyncSettings settings = SyncSettings(
            new Dictionary<string, string?>
            {
                ["InstrumentSync:IntervalMinutes"] = "60",
                ["InstrumentSync:Bybit:Enabled"] = "true",
                ["InstrumentSync:Bybit:Categories:0"] = "Spot",
                ["InstrumentSync:Bybit:Categories:1"] = "Linear"
            });

        Assert.Multiple(() =>
        {
            Assert.That(settings.IntervalMinutes, Is.EqualTo(60));
            Assert.That(settings.Bybit.Enabled, Is.True);
            Assert.That(settings.Bybit.Categories, Is.EqualTo(new[] { BybitCategory.Spot, BybitCategory.Linear }));
            Assert.That(settings.TInvest.Enabled, Is.False);
        });
    }
}