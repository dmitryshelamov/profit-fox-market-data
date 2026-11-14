using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Candles;

[TestFixture]
public class GetCandlesQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Linear], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private Task<CandlesInternal> TwoHoursAsync()
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetCandlesQuery(Linear.Id, new CandleWindow(CandleInterval.OneHour, Hour, Hour.AddHours(2))), CancellationToken.None);
    }

    [Test]
    public async Task A_candle_with_a_minute_whose_buy_volume_is_not_known_has_none()
    {
        // Both hours have two minutes; a minute of the second one came from a source that tells no sides of the trades.
        var series = CandleSeries.Create(Linear.Id);
        series.Cover(
            new MinuteRange(Hour, Hour.AddHours(2)),
            [
                new Candle(Hour, open: 5m, high: 6m, low: 4m, close: 5m, volume: 4m, turnover: 20m, buyVolume: 3m),
                new Candle(Hour.AddMinutes(1), open: 5m, high: 6m, low: 4m, close: 5m, volume: 2m, turnover: 10m, buyVolume: 0m),
                new Candle(Hour.AddMinutes(60), open: 5m, high: 6m, low: 4m, close: 5m, volume: 4m, turnover: 20m, buyVolume: 3m),
                new Candle(Hour.AddMinutes(61), open: 5m, high: 6m, low: 4m, close: 5m, volume: 2m, turnover: 10m)
            ]);
        await _scope.ServiceProvider.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);

        CandlesInternal candles = await TwoHoursAsync();

        Assert.That(candles.Candles.Select(candle => candle.BuyVolume), Is.EqualTo(new decimal?[] { 3m, null }));
    }

    [Test]
    public async Task A_candle_with_minutes_storage_does_not_hold_is_left_out()
    {
        // The second hour is covered only up to its 30th minute, though it has a stored minute.
        var series = CandleSeries.Create(Linear.Id);
        series.Cover(
            new MinuteRange(Hour, Hour.AddMinutes(90)),
            [
                new Candle(Hour, open: 5m, high: 6m, low: 4m, close: 5m, volume: 1m, turnover: 5m),
                new Candle(Hour.AddMinutes(61), open: 5m, high: 6m, low: 4m, close: 5m, volume: 1m, turnover: 5m)
            ]);
        await _scope.ServiceProvider.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);

        CandlesInternal candles = await TwoHoursAsync();

        Assert.Multiple(() =>
        {
            Assert.That(candles.Candles.Select(candle => candle.OpenTime), Is.EqualTo(new[] { Hour }));
            Assert.That(candles.Covered, Is.EqualTo(new[] { new MinuteRangeInternal(Hour, Hour.AddMinutes(90)) }));
            Assert.That(candles.Loading, Is.False);
        });
    }

    [Test]
    public async Task An_instrument_none_of_whose_minutes_is_loaded_has_no_candles_and_nothing_covered()
    {
        CandlesInternal candles = await TwoHoursAsync();

        Assert.Multiple(() =>
        {
            Assert.That(candles.Candles, Is.Empty);
            Assert.That(candles.Covered, Is.Empty);
        });
    }
}