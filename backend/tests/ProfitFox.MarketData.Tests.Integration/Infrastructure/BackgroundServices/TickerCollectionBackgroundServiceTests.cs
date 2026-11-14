using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.BackgroundServices;
using ProfitFox.MarketData.Tests.Integration.Time;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.BackgroundServices;

[TestFixture]
public class TickerCollectionBackgroundServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _sent = Channel.CreateUnbounded<CollectBybitTickersCommand>();
        _time = new WatchedTimeProvider(Start);
        _sender = Substitute.For<ISender>();
        _sender.Send(Arg.Any<CollectBybitTickersCommand>(), Arg.Any<CancellationToken>()).Returns(Collected);
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

    // Half a minute before the first boundary.
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 12, minute: 4, second: 30, TimeSpan.Zero);

    private static readonly DateTimeOffset FirstBoundary = Start.AddSeconds(30);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private Channel<CollectBybitTickersCommand> _sent = null!;
    private IConfigurationRoot _configuration = null!;
    private WatchedTimeProvider _time = null!;
    private ISender _sender = null!;
    private ServiceProvider? _services;
    private TickerCollectionBackgroundService? _service;

    // The settings as the app binds them; the sender is the last ISender registered, so it wins over the one of MediatR.
    private async Task StartAsync(bool bybit = true)
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TickerCollection:DeadlineSeconds"] = "10",
                    ["TickerCollection:Bybit:Enabled"] = bybit.ToString()
                })
            .Build();
        _services = new ServiceCollection().AddLogging().AddInfrastructure(_configuration).AddSingleton(_sender).BuildServiceProvider();
        _service = new TickerCollectionBackgroundService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IOptionsMonitor<TickerCollectionSettings>>(),
            _time,
            NullLogger<TickerCollectionBackgroundService>.Instance);

        await _service.StartAsync(CancellationToken.None);
    }

    // Like a save of appsettings.json: the value changes and the configuration tells its readers.
    private void Save(string key, string? value)
    {
        _configuration[key] = value;
        _configuration.Reload();
    }

    // A snapshot that is taken: the command is recorded.
    private Task<CollectBybitTickersResult> Collected(NSubstitute.Core.CallInfo call)
    {
        if (call[0] is CollectBybitTickersCommand command)
        {
            _sent.Writer.TryWrite(command);
        }

        return Task.FromResult(new CollectBybitTickersResult(OpenInterestPoints: 2, FundingRatePoints: 1));
    }

    private async Task<CollectBybitTickersCommand> SentAsync()
    {
        return await _sent.Reader.ReadAsync().AsTask().WaitAsync(Patience);
    }

    // The wait the service is in for its next boundary. The deadline of the snapshot before it was a wait on the clock too; the service
    // has let go of it by the time it waits for the boundary.
    private async Task<TimeSpan> WaitForBoundaryAsync()
    {
        TimeSpan wait = await _time.NextWaitAsync();

        return wait == Deadline ? await _time.NextWaitAsync() : wait;
    }

    // Lets the clock reach the next boundary, once the service waits for it.
    private async Task PassToBoundaryAsync()
    {
        _time.Advance(await WaitForBoundaryAsync());
    }

    [Test]
    public async Task A_boundary_sends_the_command_with_the_time_of_the_boundary()
    {
        await StartAsync();
        TimeSpan toBoundary = await WaitForBoundaryAsync();
        _time.Advance(toBoundary);

        CollectBybitTickersCommand sent = await SentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(toBoundary, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(sent, Is.EqualTo(new CollectBybitTickersCommand(FirstBoundary)));
        });
    }

    [Test]
    public async Task A_broken_change_keeps_the_last_valid_settings()
    {
        await StartAsync();
        await PassToBoundaryAsync();
        await SentAsync();

        // Here the reload throws to the test; in the app it fails inside the file watcher and the app runs on.
        Assert.Throws<AggregateException>(() => Save("TickerCollection:DeadlineSeconds", "60"));
        await PassToBoundaryAsync();

        Assert.That(await SentAsync(), Is.EqualTo(new CollectBybitTickersCommand(FirstBoundary + Minute)));
    }

    [Test]
    public async Task A_pause_of_Bybit_skips_the_minute_and_the_next_boundary_still_runs()
    {
        _sender.Send(Arg.Any<CollectBybitTickersCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<CollectBybitTickersResult>(new ExchangePausedException("Bybit limited the requests.", Start.AddMinutes(10))),
                Collected);

        await StartAsync();
        await PassToBoundaryAsync();
        TimeSpan toNextBoundary = await WaitForBoundaryAsync();
        _time.Advance(toNextBoundary);

        Assert.Multiple(async () =>
        {
            // The pause is not waited out: the service keeps to the boundaries.
            Assert.That(toNextBoundary, Is.EqualTo(Minute));
            Assert.That(await SentAsync(), Is.EqualTo(new CollectBybitTickersCommand(FirstBoundary + Minute)));
        });
    }

    [Test]
    public async Task A_snapshot_not_taken_by_the_deadline_is_given_up_and_the_next_boundary_still_runs()
    {
        var givenUp = new TaskCompletionSource();
        _sender.Send(Arg.Any<CollectBybitTickersCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                async call =>
                {
                    // Bybit never answers: only the token of the request ends it.
                    await using CancellationTokenRegistration registration = call.Arg<CancellationToken>().Register(givenUp.SetResult);
                    await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());

                    return new CollectBybitTickersResult(OpenInterestPoints: 0, FundingRatePoints: 0);
                },
                Collected);

        await StartAsync();
        await PassToBoundaryAsync();
        TimeSpan deadline = await _time.NextWaitAsync();
        _time.Advance(deadline - TimeSpan.FromSeconds(1));
        bool givenUpEarly = givenUp.Task.IsCompleted;
        _time.Advance(TimeSpan.FromSeconds(1));
        await givenUp.Task.WaitAsync(Patience);
        TimeSpan toNextBoundary = await WaitForBoundaryAsync();
        _time.Advance(toNextBoundary);

        Assert.Multiple(async () =>
        {
            Assert.That(deadline, Is.EqualTo(Deadline));
            Assert.That(givenUpEarly, Is.False);
            Assert.That(toNextBoundary, Is.EqualTo(Minute - Deadline));
            Assert.That(await SentAsync(), Is.EqualTo(new CollectBybitTickersCommand(FirstBoundary + Minute)));
        });
    }

    [Test]
    public async Task A_snapshot_that_fails_skips_the_minute_and_the_next_boundary_still_runs()
    {
        _sender.Send(Arg.Any<CollectBybitTickersCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<CollectBybitTickersResult>(new TransientExchangeException("Bybit answered 502.")), Collected);

        await StartAsync();
        await PassToBoundaryAsync();
        await PassToBoundaryAsync();

        Assert.Multiple(async () =>
        {
            // The minute that failed is not asked for again.
            Assert.That(await SentAsync(), Is.EqualTo(new CollectBybitTickersCommand(FirstBoundary + Minute)));
            await _sender.Received(2).Send(Arg.Any<CollectBybitTickersCommand>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Bybit_that_is_not_enabled_is_not_asked()
    {
        await StartAsync(bybit: false);
        await PassToBoundaryAsync();

        // The service waits for its next boundary: the first one is behind it.
        TimeSpan toNextBoundary = await _time.NextWaitAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(toNextBoundary, Is.EqualTo(Minute));
            await _sender.DidNotReceive().Send(Arg.Any<CollectBybitTickersCommand>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Bybit_switched_on_is_asked_from_the_next_boundary()
    {
        await StartAsync(bybit: false);
        await PassToBoundaryAsync();

        Save("TickerCollection:Bybit:Enabled", "true");
        await PassToBoundaryAsync();

        Assert.That(await SentAsync(), Is.EqualTo(new CollectBybitTickersCommand(FirstBoundary + Minute)));
    }

    [Test]
    public async Task Every_minute_boundary_sends_a_command()
    {
        await StartAsync();

        await PassToBoundaryAsync();
        CollectBybitTickersCommand first = await SentAsync();
        await PassToBoundaryAsync();
        CollectBybitTickersCommand second = await SentAsync();
        await PassToBoundaryAsync();
        CollectBybitTickersCommand third = await SentAsync();

        Assert.That(
            new[] { first, second, third },
            Is.EqualTo(
                new[]
                {
                    new CollectBybitTickersCommand(FirstBoundary),
                    new CollectBybitTickersCommand(FirstBoundary + Minute),
                    new CollectBybitTickersCommand(FirstBoundary + Minute * 2)
                }));
    }
}