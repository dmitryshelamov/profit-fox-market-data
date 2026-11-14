using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Candles;

[TestFixture]
public class GetClosedCandlesQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Linear, Spot, Share], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 2, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Spot = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateSpot(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private static readonly Instrument Share = InstrumentGenerator.Generate(
        TInvestListedShareGenerator.Generate(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000003"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private async Task StoreAsync(Instrument instrument, params Candle[] candles)
    {
        var series = CandleSeries.Create(instrument.Id);
        series.Cover(new MinuteRange(Hour.AddHours(-1), Hour.AddHours(1)), candles);
        await _scope.ServiceProvider.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
    }

    private async Task<IReadOnlyList<ClosedCandleInternal>> SinceAsync(DateTimeOffset since, params Exchange[] exchanges)
    {
        var candles = new List<ClosedCandleInternal>();

        await foreach (ClosedCandleInternal candle in _scope.ServiceProvider.GetRequiredService<ISender>()
                           .CreateStream(new GetClosedCandlesQuery(since, exchanges), CancellationToken.None))
        {
            candles.Add(candle);
        }

        return candles;
    }

    [Test]
    public async Task A_candle_comes_as_it_is_stored_with_the_exchange_of_its_instrument()
    {
        await StoreAsync(
            Linear,
            new Candle(Hour, open: 0.00001234m, high: 0.0000124m, low: 0.0000123m, close: 0.00001238m, volume: 1234567.89m, turnover: 15.2771m, buyVolume: 600000.5m));
        await StoreAsync(Share, new Candle(Hour, open: 300.5m, high: 301m, low: 300m, close: 300.75m, volume: 1000m));

        IReadOnlyList<ClosedCandleInternal> candles = await SinceAsync(Hour, Exchange.Bybit, Exchange.TInvest);

        Assert.That(
            candles,
            Is.EqualTo(
                new[]
                {
                    new ClosedCandleInternal(Exchange.Bybit, Linear.Id, Hour, 0.00001234m, 0.0000124m, 0.0000123m, 0.00001238m, 1234567.89m, 15.2771m, 600000.5m),
                    new ClosedCandleInternal(Exchange.TInvest, Share.Id, Hour, 300.5m, 301m, 300m, 300.75m, 1000m, Turnover: null, BuyVolume: null)
                }));
    }

    [Test]
    public async Task Candles_come_minute_by_minute_from_the_one_that_opens_at_since()
    {
        await StoreAsync(
            Spot,
            new Candle(Hour.AddMinutes(1), open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m),
            new Candle(Hour, open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m));
        await StoreAsync(
            Linear,
            new Candle(Hour.AddMinutes(-1), open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m),
            new Candle(Hour, open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m),
            new Candle(Hour.AddMinutes(1), open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m));

        IReadOnlyList<ClosedCandleInternal> candles = await SinceAsync(Hour, Exchange.Bybit);

        Assert.That(
            candles.Select(candle => (candle.OpenTime, candle.InstrumentId)),
            Is.EqualTo(new[] { (Hour, Linear.Id), (Hour, Spot.Id), (Hour.AddMinutes(1), Linear.Id), (Hour.AddMinutes(1), Spot.Id) }));
    }

    [Test]
    public async Task Only_the_candles_of_the_exchanges_asked_for_come()
    {
        await StoreAsync(Linear, new Candle(Hour, open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m));
        await StoreAsync(Share, new Candle(Hour, open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m));

        Assert.Multiple(async () =>
        {
            Assert.That((await SinceAsync(Hour, Exchange.Bybit)).Select(candle => candle.InstrumentId), Is.EqualTo(new[] { Linear.Id }));
            Assert.That((await SinceAsync(Hour, Exchange.TInvest)).Select(candle => candle.InstrumentId), Is.EqualTo(new[] { Share.Id }));
            Assert.That(await SinceAsync(Hour), Is.Empty);
        });
    }
}