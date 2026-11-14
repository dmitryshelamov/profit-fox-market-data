using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Postgres.Dapper.Repositories.Candles;

[TestFixture]
public class CandleSeriesRepositoryTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _repository = _services.GetRequiredService<ICandleSeriesRepository>();
        await _services.GetRequiredService<IInstrumentRepository>().SaveAsync([Linear], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Linear = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        Start.AddDays(-30),
        Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private ServiceProvider _services = null!;
    private ICandleSeriesRepository _repository = null!;

    private async Task SaveAsync(MinuteRange range, params Candle[] candles)
    {
        var series = CandleSeries.Create(Linear.Id);
        series.Cover(range, candles);
        await _repository.SaveAsync(series, CancellationToken.None);
    }

    private static async Task CompressAllChunksAsync()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);
        await connection.ExecuteAsync("SELECT compress_chunk(chunk) FROM show_chunks('candles') AS chunk");
    }

    private static async Task<IReadOnlyList<StoredCandle>> StoredCandlesAsync()
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.ConnectionString);

        return
        [
            .. await connection.QueryAsync<StoredCandle>(
                sql: """
                     SELECT open_time AS "OpenTime", open, high, low, close, volume, turnover, buy_volume AS "BuyVolume"
                     FROM candles
                     WHERE instrument_id = @InstrumentId
                     ORDER BY open_time
                     """,
                new { InstrumentId = Linear.Id })
        ];
    }

    private static Candle CandleAt(DateTimeOffset openTime, decimal close)
    {
        return CandleGenerator.Generate(openTime, close);
    }

    private static MinuteRange Range(DateTimeOffset from, int minutes)
    {
        return new MinuteRange(from, from.AddMinutes(minutes));
    }

    private sealed record StoredCandle
    {
        public DateTimeOffset OpenTime { get; init; }

        public decimal Open { get; init; }

        public decimal High { get; init; }

        public decimal Low { get; init; }

        public decimal Close { get; init; }

        public decimal Volume { get; init; }

        public decimal? Turnover { get; init; }

        public decimal? BuyVolume { get; init; }

        public static StoredCandle Of(Candle candle)
        {
            return new StoredCandle
            {
                OpenTime = candle.OpenTime,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume,
                Turnover = candle.Turnover,
                BuyVolume = candle.BuyVolume
            };
        }
    }

    [Test]
    public async Task A_buy_volume_stays_when_the_minute_is_saved_again_without_one()
    {
        await SaveAsync(Range(Start, minutes: 1), CandleGenerator.Generate(Start, close: 5m, volume: 3m, buyVolume: 2m));

        await SaveAsync(Range(Start, minutes: 1), CandleGenerator.Generate(Start, close: 6m, volume: 3m));

        StoredCandle stored = (await StoredCandlesAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.Close, Is.EqualTo(6m));
            Assert.That(stored.BuyVolume, Is.EqualTo(2m));
        });
    }

    [Test]
    public async Task A_buy_volume_takes_the_value_a_later_save_knows()
    {
        await SaveAsync(
            Range(Start, minutes: 2),
            CandleGenerator.Generate(Start, volume: 3m),
            CandleGenerator.Generate(Start.AddMinutes(1), volume: 3m, buyVolume: 2m));

        await SaveAsync(
            Range(Start, minutes: 2),
            CandleGenerator.Generate(Start, volume: 3m, buyVolume: 1m),
            CandleGenerator.Generate(Start.AddMinutes(1), volume: 3m, buyVolume: 0m));

        Assert.That((await StoredCandlesAsync()).Select(candle => candle.BuyVolume), Is.EqualTo(new decimal?[] { 1m, 0m }));
    }

    [Test]
    public async Task A_candle_of_a_compressed_chunk_takes_the_new_values()
    {
        DateTimeOffset monthAgo = Start.AddDays(-20);
        await SaveAsync(Range(monthAgo, minutes: 2), CandleAt(monthAgo, close: 5m));
        await CompressAllChunksAsync();

        await SaveAsync(Range(monthAgo, minutes: 2), CandleAt(monthAgo, close: 6m));

        Assert.That((await StoredCandlesAsync()).Select(candle => candle.Close), Is.EqualTo(new[] { 6m }));
    }

    [Test]
    public async Task A_candle_of_a_stored_minute_takes_the_new_values()
    {
        await SaveAsync(Range(Start, minutes: 2), CandleAt(Start, close: 5m));

        await SaveAsync(Range(Start, minutes: 2), CandleAt(Start, close: 6m), CandleAt(Start.AddMinutes(1), close: 7m));

        Assert.That((await StoredCandlesAsync()).Select(candle => candle.Close), Is.EqualTo(new[] { 6m, 7m }));
    }

    [Test]
    public async Task A_saved_series_comes_back_with_its_ranges_and_no_new_candles()
    {
        var series = CandleSeries.Create(Linear.Id);
        series.Cover(Range(Start.AddHours(-5), minutes: 60), [CandleAt(Start.AddHours(-5), close: 5m)]);
        series.Cover(Range(Start, minutes: 30), []);
        await _repository.SaveAsync(series, CancellationToken.None);

        CandleSeries? stored = await _repository.GetAsync(Linear.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(stored?.Covered, Is.EqualTo(new[] { Range(Start.AddHours(-5), minutes: 60), Range(Start, minutes: 30) }));
            Assert.That(stored?.NewCandles, Is.Empty);
        });
    }

    [Test]
    public async Task A_series_never_saved_is_not_found()
    {
        Assert.That(await _repository.GetAsync(Linear.Id, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task A_series_that_grows_on_takes_the_ranges_it_had_and_the_new_one()
    {
        await SaveAsync(Range(Start, minutes: 10));
        CandleSeries stored = (await _repository.GetAsync(Linear.Id, CancellationToken.None))!;

        stored.Cover(Range(Start.AddMinutes(10), minutes: 10), []);
        await _repository.SaveAsync(stored, CancellationToken.None);

        CandleSeries? grown = await _repository.GetAsync(Linear.Id, CancellationToken.None);
        Assert.That(grown?.Covered, Is.EqualTo(new[] { Range(Start, minutes: 20) }));
    }

    [Test]
    public async Task Candles_are_stored_exactly_as_the_exchange_gave_them()
    {
        var bybit = new Candle(Start, open: 0.00001234m, high: 0.00001240m, low: 0.00001230m, close: 0.00001238m, volume: 1234567.89m, turnover: 15.2771m);
        var tinvest = new Candle(Start.AddMinutes(1), open: 300.5m, high: 301m, low: 300m, close: 300.75m, volume: 1000m, buyVolume: 612.5m);

        await SaveAsync(Range(Start, minutes: 2), bybit, tinvest);

        Assert.That(await StoredCandlesAsync(), Is.EqualTo(new[] { StoredCandle.Of(bybit), StoredCandle.Of(tinvest) }));
    }

    [Test]
    public async Task Two_loaders_that_read_the_same_series_both_keep_what_they_covered()
    {
        await SaveAsync(Range(Start, minutes: 10));
        CandleSeries background = (await _repository.GetAsync(Linear.Id, CancellationToken.None))!;
        CandleSeries read = (await _repository.GetAsync(Linear.Id, CancellationToken.None))!;

        background.Cover(Range(Start.AddMinutes(10), minutes: 10), []);
        read.Cover(Range(Start.AddMinutes(-10), minutes: 10), []);
        await _repository.SaveAsync(background, CancellationToken.None);
        await _repository.SaveAsync(read, CancellationToken.None);

        CandleSeries? stored = await _repository.GetAsync(Linear.Id, CancellationToken.None);
        Assert.That(stored?.Covered, Is.EqualTo(new[] { Range(Start.AddMinutes(-10), minutes: 30) }));
    }
}