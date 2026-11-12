using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class InstrumentSyncBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _sent = Channel.CreateUnbounded<SyncBybitInstrumentsCommand>();
        _time = new FakeTimeProvider(Start);
        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<SyncBybitInstrumentsCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call[0] is SyncBybitInstrumentsCommand command)
                {
                    _sent.Writer.TryWrite(command);
                }

                return Task.CompletedTask;
            });
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_service is not null)
        {
            await _service.StopAsync(CancellationToken.None);
            _service.Dispose();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private Channel<SyncBybitInstrumentsCommand> _sent = null!;
    private IConfigurationRoot _configuration = null!;
    private FakeTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider? _services;
    private InstrumentSyncBackgroundService? _service;

    // The settings as the app binds them, every 60 minutes; the sender is the last ISender registered, so it wins over the one of MediatR.
    private async Task StartAsync(params string[] categories)
    {
        var values = new Dictionary<string, string?> { ["InstrumentSync:IntervalMinutes"] = "60" };

        for (var index = 0; index < categories.Length; index++)
        {
            values[$"InstrumentSync:Categories:{index}"] = categories[index];
        }

        _configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(_configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new InstrumentSyncBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IOptionsMonitor<InstrumentSyncSettings>>(),
            _time,
            NullLogger<InstrumentSyncBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
    }

    // Like a save of appsettings.json: the value changes and the configuration tells its readers.
    private void Save(string key, string? value)
    {
        _configuration[key] = value;
        _configuration.Reload();
    }

    private async Task<IReadOnlyList<SyncBybitInstrumentsCommand>> SentAsync(int count)
    {
        var sent = new List<SyncBybitInstrumentsCommand>();

        while (sent.Count < count)
        {
            sent.Add(await _sent.Reader.ReadAsync().AsTask().WaitAsync(Wait));
        }

        return sent;
    }

    [Test]
    public async Task A_broken_change_keeps_the_last_valid_settings()
    {
        await StartAsync("Spot", "Linear");
        await SentAsync(count: 2);

        // Here the reload throws to the test; in the app it fails inside the file watcher and the app runs on.
        Assert.Throws<AggregateException>(() => Save("InstrumentSync:IntervalMinutes", "0"));
        _time.Advance(TimeSpan.FromMinutes(60));

        Assert.That(
            await SentAsync(count: 2),
            Is.EqualTo(
                new[]
                {
                    new SyncBybitInstrumentsCommand(BybitCategory.Spot, Start.AddHours(1)),
                    new SyncBybitInstrumentsCommand(BybitCategory.Linear, Start.AddHours(1))
                }));
    }

    [Test]
    public async Task A_category_that_fails_does_not_stop_the_others()
    {
        _sender.Send(Arg.Is<SyncBybitInstrumentsCommand>(command => command != null && command.Category == BybitCategory.Spot), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("Bybit is down.")));

        await StartAsync("Spot", "Linear");

        Assert.That(await SentAsync(count: 1), Is.EqualTo(new[] { new SyncBybitInstrumentsCommand(BybitCategory.Linear, Start) }));
    }

    [Test]
    public async Task A_new_interval_applies_after_the_current_wait()
    {
        await StartAsync("Spot");
        await SentAsync(count: 1);
        _time.Advance(TimeSpan.FromMinutes(10));

        Save("InstrumentSync:IntervalMinutes", "5");
        _time.Advance(TimeSpan.FromMinutes(50));
        IReadOnlyList<SyncBybitInstrumentsCommand> afterCurrentWait = await SentAsync(count: 1);
        _time.Advance(TimeSpan.FromMinutes(5));
        IReadOnlyList<SyncBybitInstrumentsCommand> afterNewInterval = await SentAsync(count: 1);

        Assert.Multiple(() =>
        {
            Assert.That(afterCurrentWait, Is.EqualTo(new[] { new SyncBybitInstrumentsCommand(BybitCategory.Spot, Start.AddMinutes(60)) }));
            Assert.That(afterNewInterval, Is.EqualTo(new[] { new SyncBybitInstrumentsCommand(BybitCategory.Spot, Start.AddMinutes(65)) }));
        });
    }

    [Test]
    public async Task Every_category_syncs_when_the_app_starts()
    {
        await StartAsync("Spot", "Linear");

        Assert.That(
            await SentAsync(count: 2),
            Is.EqualTo(
                new[]
                {
                    new SyncBybitInstrumentsCommand(BybitCategory.Spot, Start),
                    new SyncBybitInstrumentsCommand(BybitCategory.Linear, Start)
                }));
    }

    [Test]
    public async Task New_categories_go_into_the_next_sync()
    {
        await StartAsync("Spot");
        await SentAsync(count: 1);

        Save("InstrumentSync:Categories:1", "Linear");
        _time.Advance(TimeSpan.FromMinutes(60));

        Assert.That(
            await SentAsync(count: 2),
            Is.EqualTo(
                new[]
                {
                    new SyncBybitInstrumentsCommand(BybitCategory.Spot, Start.AddHours(1)),
                    new SyncBybitInstrumentsCommand(BybitCategory.Linear, Start.AddHours(1))
                }));
    }

    [Test]
    public async Task The_next_sync_comes_after_the_interval()
    {
        await StartAsync("Spot", "Linear");
        await SentAsync(count: 2);

        _time.Advance(TimeSpan.FromMinutes(59));
        bool syncedEarly = _sent.Reader.TryRead(out _);
        _time.Advance(TimeSpan.FromMinutes(1));
        IReadOnlyList<SyncBybitInstrumentsCommand> sent = await SentAsync(count: 2);

        Assert.Multiple(() =>
        {
            Assert.That(syncedEarly, Is.False);
            Assert.That(
                sent,
                Is.EqualTo(
                    new[]
                    {
                        new SyncBybitInstrumentsCommand(BybitCategory.Spot, Start.AddHours(1)),
                        new SyncBybitInstrumentsCommand(BybitCategory.Linear, Start.AddHours(1))
                    }));
        });
    }
}