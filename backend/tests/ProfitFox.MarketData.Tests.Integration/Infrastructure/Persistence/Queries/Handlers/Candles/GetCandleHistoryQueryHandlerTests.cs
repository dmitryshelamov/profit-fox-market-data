using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
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
public class GetCandleHistoryQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth, Share], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Day = new DateTimeOffset(year: 2026, month: 11, day: 10, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Ten = Day.AddHours(10);

    // The ids put BTC before ETH among the candles of one time.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Eth = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear("ETH"),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private static readonly Instrument Share = InstrumentGenerator.Generate(
        TInvestListedShareGenerator.Generate(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000003"));

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private static Candle Flat(DateTimeOffset openTime, decimal price = 5m)
    {
        return new Candle(openTime, open: price, high: price, low: price, close: price, volume: 1m, turnover: price, buyVolume: 0.5m);
    }

    private static InstrumentCoverageInternal Covered(Instrument instrument, DateTimeOffset from, DateTimeOffset until)
    {
        return new InstrumentCoverageInternal(instrument.Id, [new TimeRangeInternal(from, until)]);
    }

    private static Task StoreAsync(IServiceProvider services, Instrument instrument, params Candle[] candles)
    {
        var series = CandleSeries.Create(instrument.Id);

        foreach (Candle candle in candles)
        {
            series.Cover(new MinuteRange(candle.OpenTime, candle.OpenTime.AddMinutes(1)), [candle]);
        }

        return services.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
    }

    private Task StoreAsync(Instrument instrument, params Candle[] candles)
    {
        return StoreAsync(_scope.ServiceProvider, instrument, candles);
    }

    private IAsyncEnumerable<ClosedCandleInternal> Stream(CandleInterval interval, params InstrumentCoverageInternal[] coverage)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>().CreateStream(new GetCandleHistoryQuery(coverage, interval));
    }

    private async Task<IReadOnlyList<ClosedCandleInternal>> ReadAsync(CandleInterval interval, params InstrumentCoverageInternal[] coverage)
    {
        var read = new List<ClosedCandleInternal>();

        await foreach (ClosedCandleInternal candle in Stream(interval, coverage))
        {
            read.Add(candle);
        }

        return read;
    }

    [Test]
    public async Task A_candle_of_five_minutes_is_built_from_its_minutes()
    {
        await StoreAsync(
            Btc,
            new Candle(Ten, open: 5m, high: 6m, low: 4m, close: 5.5m, volume: 1m, turnover: 5m, buyVolume: 0.25m),
            new Candle(Ten.AddMinutes(1), open: 5.5m, high: 8m, low: 5m, close: 7m, volume: 2m, turnover: 14m, buyVolume: 1.5m));

        IReadOnlyList<ClosedCandleInternal> candles = await ReadAsync(CandleInterval.FiveMinutes, Covered(Btc, Ten, Ten.AddMinutes(5)));

        Assert.That(
            candles,
            Is.EqualTo(
                new[]
                {
                    new ClosedCandleInternal(
                        Exchange.Bybit,
                        Btc.Id,
                        Ten,
                        Open: 5m,
                        High: 8m,
                        Low: 4m,
                        Close: 7m,
                        Volume: 3m,
                        Turnover: 19m,
                        BuyVolume: 1.75m)
                }));
    }

    [Test]
    public async Task A_candle_with_a_minute_without_buy_volume_has_none()
    {
        await StoreAsync(Btc, Flat(Ten), new Candle(Ten.AddMinutes(1), open: 5m, high: 5m, low: 5m, close: 5m, volume: 2m, turnover: 10m));

        IReadOnlyList<ClosedCandleInternal> candles = await ReadAsync(CandleInterval.OneHour, Covered(Btc, Ten, Ten.AddHours(1)));

        Assert.That(candles.Single().BuyVolume, Is.Null);
    }

    [Test]
    public async Task Only_a_candle_whose_minutes_are_all_covered_comes()
    {
        await StoreAsync(Btc, Flat(Ten), Flat(Ten.AddHours(1)));

        IReadOnlyList<ClosedCandleInternal> candles = await ReadAsync(CandleInterval.OneHour, Covered(Btc, Ten, Ten.AddMinutes(90)));

        Assert.That(candles.Select(candle => candle.OpenTime), Is.EqualTo(new[] { Ten }));
    }

    [Test]
    public async Task A_minute_stored_outside_the_coverage_given_does_not_come()
    {
        await StoreAsync(Btc, Flat(Ten.AddHours(-1)), Flat(Ten));

        IReadOnlyList<ClosedCandleInternal> candles = await ReadAsync(CandleInterval.OneMinute, Covered(Btc, Ten, Ten.AddHours(1)));

        Assert.That(candles.Select(candle => candle.OpenTime), Is.EqualTo(new[] { Ten }));
    }

    [Test]
    public async Task Candles_come_by_open_time_and_then_by_instrument_id()
    {
        await StoreAsync(Eth, Flat(Ten), Flat(Ten.AddHours(1)));
        await StoreAsync(Btc, Flat(Ten), Flat(Ten.AddHours(1)));

        IReadOnlyList<ClosedCandleInternal> candles = await ReadAsync(
            CandleInterval.OneHour,
            Covered(Btc, Ten, Ten.AddHours(2)),
            Covered(Eth, Ten, Ten.AddHours(2)));

        Assert.That(
            candles.Select(candle => (candle.OpenTime, candle.InstrumentId)),
            Is.EqualTo(new[] { (Ten, Btc.Id), (Ten, Eth.Id), (Ten.AddHours(1), Btc.Id), (Ten.AddHours(1), Eth.Id) }));
    }

    [Test]
    public async Task A_minute_of_a_share_comes_without_turnover_and_its_hour_with_the_estimate()
    {
        await StoreAsync(Share, new Candle(Ten, open: 2m, high: 3m, low: 1m, close: 2m, volume: 10m));

        Assert.Multiple(async () =>
        {
            Assert.That((await ReadAsync(CandleInterval.OneMinute, Covered(Share, Ten, Ten.AddHours(1)))).Single().Turnover, Is.Null);
            Assert.That((await ReadAsync(CandleInterval.OneHour, Covered(Share, Ten, Ten.AddHours(1)))).Single().Turnover, Is.EqualTo(20m));
        });
    }

    [Test]
    public async Task The_days_of_a_window_come_one_after_another()
    {
        await StoreAsync(Btc, Flat(Day.AddDays(2)), Flat(Day), Flat(Day.AddDays(1)));

        IReadOnlyList<ClosedCandleInternal> candles = await ReadAsync(CandleInterval.OneDay, Covered(Btc, Day, Day.AddDays(3)));

        Assert.That(candles.Select(candle => candle.OpenTime), Is.EqualTo(new[] { Day, Day.AddDays(1), Day.AddDays(2) }));
    }

    [Test]
    public async Task A_read_takes_one_core_and_its_settings_do_not_reach_the_next_request_of_the_pool()
    {
        await StoreAsync(Btc, Flat(Ten));
        await ReadAsync(CandleInterval.OneMinute, Covered(Btc, Ten, Ten.AddHours(1)));

        NpgsqlConnection read = _scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
        string workers = await read.ExecuteScalarAsync<string>("SHOW max_parallel_workers_per_gather") ?? "";
        string memory = await read.ExecuteScalarAsync<string>("SHOW work_mem") ?? "";
        await _scope.DisposeAsync();

        _scope = _services.CreateAsyncScope();
        string next = await _scope.ServiceProvider.GetRequiredService<NpgsqlConnection>().ExecuteScalarAsync<string>("SHOW work_mem") ?? "";

        Assert.Multiple(() =>
        {
            Assert.That(workers, Is.EqualTo("0"));
            Assert.That(memory, Is.EqualTo("256MB"));
            Assert.That(next, Is.Not.EqualTo("256MB"));
        });
    }

    [Test]
    public async Task A_day_is_read_only_once_the_rows_of_the_day_before_are_taken()
    {
        await StoreAsync(Btc, Flat(Ten), Flat(Ten.AddMinutes(1)));

        await using IAsyncEnumerator<ClosedCandleInternal> candles = Stream(CandleInterval.OneMinute, Covered(Btc, Day, Day.AddDays(2))).GetAsyncEnumerator();
        Assert.That(await candles.MoveNextAsync(), Is.True);

        // Stored after the first row of the first day was taken: a read that held the window, or ran ahead of its caller, would miss it.
        await using (AsyncServiceScope other = _services.CreateAsyncScope())
        {
            await StoreAsync(other.ServiceProvider, Btc, Flat(Ten.AddDays(1)));
        }

        var rest = new List<DateTimeOffset>();

        while (await candles.MoveNextAsync())
        {
            rest.Add(candles.Current.OpenTime);
        }

        Assert.That(rest, Is.EqualTo(new[] { Ten.AddMinutes(1), Ten.AddDays(1) }));
    }
}