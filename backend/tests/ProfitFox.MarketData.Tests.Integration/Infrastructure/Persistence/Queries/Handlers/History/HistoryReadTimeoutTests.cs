using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Tests.Integration.Settings;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     The wait of the statements of history is theirs alone: the other statements of the same scope, on the same connection, keep the
///     timeout of the connection string.
/// </summary>
[TestFixture]
public class HistoryReadTimeoutTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services(services => services.AddSingleton<IOptionsMonitor<HistoryReadSettings>>(
            new FixedOptionsMonitor<HistoryReadSettings>(new HistoryReadSettings { StatementTimeoutSeconds = 1 })));
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Day = new DateTimeOffset(year: 2026, month: 11, day: 10, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private async Task ReadCandlesAsync()
    {
        InstrumentCoverageInternal[] coverage = [new InstrumentCoverageInternal(Btc.Id, [new TimeRangeInternal(Day, Day.AddDays(1))])];

        await foreach (ClosedCandleInternal _ in _scope.ServiceProvider.GetRequiredService<ISender>()
                           .CreateStream(new GetCandleHistoryQuery(coverage, CandleInterval.OneMinute)))
        {
        }
    }

    [Test]
    public async Task A_write_in_the_scope_of_a_read_of_history_waits_longer_than_the_read()
    {
        await using NpgsqlConnection locked = await IntegrationEnvironment.LockCandlesAsync();
        Assert.ThrowsAsync<StorageTimeoutException>(ReadCandlesAsync);

        var series = CandleSeries.Create(Btc.Id);
        series.Cover(new MinuteRange(Day, Day.AddMinutes(1)), [new Candle(Day, open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m, turnover: 5m, buyVolume: 0.5m)]);
        Task saving = _scope.ServiceProvider.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        bool waited = !saving.IsCompleted;
        await locked.DisposeAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(waited, Is.True);
            await Assert.ThatAsync(() => saving.WaitAsync(Patience), Throws.Nothing);
        });
    }
}