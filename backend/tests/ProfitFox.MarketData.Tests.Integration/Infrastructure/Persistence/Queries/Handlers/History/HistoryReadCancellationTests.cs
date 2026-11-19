using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.History;

/// <summary>
///     A read of history ends each of its statements without asking Postgres to cancel it: a request of cancellation opens a
///     connection of its own to the server, and a read of years makes hundreds of statements.
/// </summary>
[TestFixture]
public class HistoryReadCancellationTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _logs = new FakeLoggerProvider();
        _loggers = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(_logs));
        _services = IntegrationEnvironment.Services(services => services.AddSingleton(
            _ => new NpgsqlDataSourceBuilder(IntegrationEnvironment.ConnectionString).UseLoggerFactory(_loggers).Build()));
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        _loggers.Dispose();
        _logs.Dispose();
    }

    // The event Npgsql logs when it sends a request of cancellation to the server.
    private const int CancellationRequestSent = 2002;

    private static readonly DateTimeOffset Day = new DateTimeOffset(year: 2026, month: 11, day: 10, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly DateTimeOffset[] Times = [Day.AddHours(8), Day.AddDays(1).AddHours(8), Day.AddDays(2).AddHours(8)];

    private FakeLoggerProvider _logs = null!;
    private ILoggerFactory _loggers = null!;
    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private int CancellationRequests => _logs.Collector.GetSnapshot().Count(record => record.Id.Id == CancellationRequestSent);

    private static InstrumentCoverageInternal[] ThreeDays()
    {
        return [new InstrumentCoverageInternal(Btc.Id, [new TimeRangeInternal(Day, Day.AddDays(3))])];
    }

    [Test]
    public async Task A_read_of_candles_of_three_days_sends_no_request_of_cancellation()
    {
        var series = CandleSeries.Create(Btc.Id);

        foreach (DateTimeOffset time in Times)
        {
            series.Cover(new MinuteRange(time, time.AddMinutes(1)), [new Candle(time, 5m, 5m, 5m, 5m, volume: 1m, turnover: 5m, buyVolume: 0.5m)]);
        }

        await _scope.ServiceProvider.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
        var read = new List<DateTimeOffset>();

        await foreach (ClosedCandleInternal candle in _scope.ServiceProvider.GetRequiredService<ISender>()
                           .CreateStream(new GetCandleHistoryQuery(ThreeDays(), CandleInterval.OneMinute)))
        {
            read.Add(candle.OpenTime);
        }

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(Times));
            Assert.That(CancellationRequests, Is.Zero);
        });
    }

    [Test]
    public async Task A_read_of_funding_payments_of_three_days_sends_no_request_of_cancellation()
    {
        await _scope.ServiceProvider.GetRequiredService<IFundingPaymentSeriesRepository>()
            .SaveAsync(
                FundingPaymentSeries.Create(Btc.Id, Day, Day.AddDays(3), [.. Times.Select(time => new FundingPayment(time, 0.0001m))]),
                CancellationToken.None);
        var read = new List<DateTimeOffset>();

        await foreach (InstrumentFundingPaymentInternal payment in _scope.ServiceProvider.GetRequiredService<ISender>()
                           .CreateStream(new GetFundingPaymentHistoryQuery(ThreeDays())))
        {
            read.Add(payment.Time);
        }

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(Times));
            Assert.That(CancellationRequests, Is.Zero);
        });
    }
}