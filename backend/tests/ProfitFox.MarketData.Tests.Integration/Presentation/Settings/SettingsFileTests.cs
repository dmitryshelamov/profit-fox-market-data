using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Presentation.Settings;

namespace ProfitFox.MarketData.Tests.Integration.Presentation.Settings;

[TestFixture]
public class SettingsFileTests
{
    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("profit-fox-market-data-");
        _file = Path.Combine(_root.CreateSubdirectory("config").FullName, "market-data.json");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        _root.Delete(recursive: true);
    }

    private DirectoryInfo _root = null!;
    private string _file = null!;
    private ServiceProvider? _services;

    // The settings of the image and of the environment come first; the file of the content root is added over them, as the app does.
    private IOptionsMonitor<BybitApiSettings> Bybit()
    {
        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(_root.FullName)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BybitApi:LimitPauseSeconds"] = "5" });
        SettingsFile.AddTo(builder);
        _services = new ServiceCollection().AddLogging().AddInfrastructure(builder.Build()).BuildServiceProvider();

        return _services.GetRequiredService<IOptionsMonitor<BybitApiSettings>>();
    }

    [Test]
    public async Task A_change_of_the_file_changes_a_setting_the_app_reads_live_without_a_restart()
    {
        await File.WriteAllTextAsync(_file, """{ "BybitApi": { "LimitPauseSeconds": 7 } }""");
        IOptionsMonitor<BybitApiSettings> bybit = Bybit();
        int before = bybit.CurrentValue.LimitPauseSeconds;
        var changed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable? listener = bybit.OnChange(current => changed.TrySetResult(current.LimitPauseSeconds));

        await File.WriteAllTextAsync(_file, """{ "BybitApi": { "LimitPauseSeconds": 9 } }""");

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.EqualTo(7));
            Assert.That(await changed.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(9));
        });
    }

    // A broken edit must not keep the app from starting again, as after a reboot of the server.
    [Test]
    public async Task A_file_that_is_not_valid_JSON_at_start_leaves_the_settings_of_the_image()
    {
        await File.WriteAllTextAsync(_file, """{ "BybitApi": { "LimitPauseSeconds": """);

        Assert.That(Bybit().CurrentValue.LimitPauseSeconds, Is.EqualTo(5));
    }

    [Test]
    public void No_file_leaves_the_settings_of_the_image()
    {
        Assert.That(Bybit().CurrentValue.LimitPauseSeconds, Is.EqualTo(5));
    }
}