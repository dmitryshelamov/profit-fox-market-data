using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;
using ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;
using ProfitFox.MarketData.Presentation.Settings;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Exchanges;

/// <summary>
///     The settings of the exchanges as the settings file changes them on the server: the file is written, and its watcher reloads it on
///     a thread of its own, which is where a broken value is first checked.
/// </summary>
[TestFixture]
public class LastValidOptionsMonitorTests
{
    [SetUp]
    public void SetUp()
    {
        _errors = new ErrorCounter();
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

        _errors.Dispose();
        _root.Delete(recursive: true);
    }

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private ErrorCounter _errors = null!;
    private DirectoryInfo _root = null!;
    private string _file = null!;
    private ServiceProvider? _services;

    // The app's registration over the settings file, which starts with json.
    private async Task<IOptionsMonitor<TSettings>> SettingsAsync<TSettings>(string json)
        where TSettings : class
    {
        await File.WriteAllTextAsync(_file, json);
        IConfigurationBuilder builder = new ConfigurationBuilder().SetBasePath(_root.FullName);
        SettingsFile.AddTo(builder);
        _services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(_errors))
            .AddInfrastructure(builder.Build())
            .BuildServiceProvider();

        return _services.GetRequiredService<IOptionsMonitor<TSettings>>();
    }

    // Writes json over the file and waits until reading the settings has logged errors in all.
    private async Task BreakAsync<TSettings>(IOptionsMonitor<TSettings> settings, string json, int errors)
    {
        await File.WriteAllTextAsync(_file, json);
        DateTime until = DateTime.UtcNow + Patience;

        while (_errors.Count < errors && DateTime.UtcNow < until)
        {
            _ = settings.CurrentValue;
            await Task.Delay(millisecondsDelay: 20);
        }
    }

    private async Task<int> ChangedAsync(IOptionsMonitor<BybitApiSettings> bybit, string json)
    {
        var changed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable? listener = bybit.OnChange(current => changed.TrySetResult(current.RequestsPerSecond));
        await File.WriteAllTextAsync(_file, json);

        return await changed.Task.WaitAsync(Patience);
    }

    [Test]
    public async Task A_change_that_breaks_the_settings_keeps_the_last_valid_ones_and_is_logged_once()
    {
        IOptionsMonitor<BybitApiSettings> bybit = await SettingsAsync<BybitApiSettings>("""{ "BybitApi": { "RequestsPerSecond": 10 } }""");
        int before = bybit.CurrentValue.RequestsPerSecond;

        await BreakAsync(bybit, """{ "BybitApi": { "RequestsPerSecond": 0 } }""", errors: 1);
        int whileBroken = bybit.CurrentValue.RequestsPerSecond;

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(10));
            Assert.That(whileBroken, Is.EqualTo(10));
            Assert.That(_errors.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_fixed_change_applies_and_a_later_break_is_logged_again()
    {
        IOptionsMonitor<BybitApiSettings> bybit = await SettingsAsync<BybitApiSettings>("""{ "BybitApi": { "RequestsPerSecond": 10 } }""");
        _ = bybit.CurrentValue;
        await BreakAsync(bybit, """{ "BybitApi": { "RequestsPerSecond": 0 } }""", errors: 1);

        int fixedValue = await ChangedAsync(bybit, """{ "BybitApi": { "RequestsPerSecond": 7 } }""");
        await BreakAsync(bybit, """{ "BybitApi": { "RequestsPerSecond": -1 } }""", errors: 2);

        Assert.Multiple(() =>
        {
            Assert.That(fixedValue, Is.EqualTo(7));
            Assert.That(bybit.CurrentValue.RequestsPerSecond, Is.EqualTo(7));
            Assert.That(_errors.Count, Is.EqualTo(2));
        });
    }

    // The clients of T-Invest read the token through the same settings on every call.
    [Test]
    public async Task A_broken_change_of_the_TInvest_settings_keeps_the_last_valid_ones()
    {
        IOptionsMonitor<TInvestApiSettings> tinvest = await SettingsAsync<TInvestApiSettings>("""{ "TInvestApi": { "MarketDataRequestsPerMinute": 100 } }""");
        _ = tinvest.CurrentValue;

        await BreakAsync(tinvest, """{ "TInvestApi": { "MarketDataRequestsPerMinute": 0 } }""", errors: 1);

        Assert.That(tinvest.CurrentValue.MarketDataRequestsPerMinute, Is.EqualTo(100));
    }

    [Test]
    public async Task Settings_broken_from_the_start_still_stop_the_app()
    {
        IOptionsMonitor<BybitApiSettings> bybit = await SettingsAsync<BybitApiSettings>("""{ "BybitApi": { "RequestsPerSecond": 0 } }""");

        Assert.Throws<OptionsValidationException>(() => _ = bybit.CurrentValue);
    }

    private sealed class ErrorCounter : ILoggerProvider, ILogger
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public ILogger CreateLogger(string categoryName)
        {
            return categoryName.StartsWith("ProfitFox.MarketData.Infrastructure.Exchanges.LastValidOptionsMonitor", StringComparison.Ordinal) ? this : NullLogger.Instance;
        }

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Interlocked.Increment(ref _count);
            }
        }
    }
}