using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
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

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private DirectoryInfo _root = null!;
    private string _file = null!;
    private IConfiguration _configuration = null!;
    private ServiceProvider? _services;

    // The settings of the image and of the environment come first; the file of the content root is added over them, as the app does.
    private IOptionsMonitor<TSettings> Settings<TSettings>()
        where TSettings : class
    {
        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(_root.FullName)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BybitApi:LimitPauseSeconds"] = "5" });
        SettingsFile.AddTo(builder);
        _configuration = builder.Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(_configuration).BuildServiceProvider();

        return _services.GetRequiredService<IOptionsMonitor<TSettings>>();
    }

    private IOptionsMonitor<BybitApiSettings> Bybit()
    {
        return Settings<BybitApiSettings>();
    }

    // Writes json over the file and waits until the configuration holds the value at key.
    private async Task WriteAsync(string json, string key, string value)
    {
        await File.WriteAllTextAsync(_file, json);
        DateTime until = DateTime.UtcNow + Patience;

        while (_configuration[key] != value && DateTime.UtcNow < until)
        {
            await Task.Delay(millisecondsDelay: 20);
        }

        Assert.That(_configuration[key], Is.EqualTo(value));
    }

    [Test]
    public async Task A_broken_change_of_the_history_reads_keeps_the_last_valid_ones_and_a_fixed_one_applies()
    {
        await File.WriteAllTextAsync(_file, """{ "HistoryRead": { "StatementTimeoutSeconds": 120 } }""");
        IOptionsMonitor<HistoryReadSettings> history = Settings<HistoryReadSettings>();
        int before = history.CurrentValue.StatementTimeoutSeconds;

        await WriteAsync("""{ "HistoryRead": { "StatementTimeoutSeconds": 0, "MaxConcurrentReads": 5 } }""", "HistoryRead:StatementTimeoutSeconds", "0");
        HistoryReadSettings whileBroken = history.CurrentValue;

        var changed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable? listener = history.OnChange(current => changed.TrySetResult(current.StatementTimeoutSeconds));
        await File.WriteAllTextAsync(_file, """{ "HistoryRead": { "StatementTimeoutSeconds": 300 } }""");

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.EqualTo(120));
            Assert.That(whileBroken.StatementTimeoutSeconds, Is.EqualTo(120));
            Assert.That(whileBroken.MaxConcurrentReads, Is.EqualTo(2));
            Assert.That(await changed.Task.WaitAsync(Patience), Is.EqualTo(300));
            Assert.That(history.CurrentValue.StatementTimeoutSeconds, Is.EqualTo(300));
        });
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