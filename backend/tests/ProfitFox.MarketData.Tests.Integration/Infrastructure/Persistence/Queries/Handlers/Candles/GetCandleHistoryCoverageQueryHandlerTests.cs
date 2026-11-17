using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Exceptions;
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
public class GetCandleHistoryCoverageQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();

        Instrument delisted = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"), id: DelistedId);
        delisted.Delist(Today.AddDays(-1));
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Share, delisted], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Today = new DateTimeOffset(year: 2026, month: 11, day: 17, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = Today.AddHours(6);

    private static readonly DateTimeOffset Day = Today.AddDays(-3);

    // The ids put BTC before the share and the share before ETH.
    private static readonly Instrument Btc = InstrumentGenerator.Generate(
        BybitListedInstrumentGenerator.GenerateLinear(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000001"));

    private static readonly Instrument Share = InstrumentGenerator.Generate(
        TInvestListedShareGenerator.Generate(),
        id: Guid.Parse("0193a1f0-0000-7000-8000-000000000002"));

    private static readonly Guid DelistedId = Guid.Parse("0193a1f0-0000-7000-8000-000000000003");

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;

    private async Task StoreAsync(Guid instrumentId, MinuteRange covered, MinuteRange? settled = null)
    {
        var series = CandleSeries.Create(instrumentId);
        series.Cover(covered, [new Candle(covered.From, open: 5m, high: 5m, low: 5m, close: 5m, volume: 1m)]);

        if (settled is not null)
        {
            series.Settle(settled, []);
        }

        await _scope.ServiceProvider.GetRequiredService<ICandleSeriesRepository>().SaveAsync(series, CancellationToken.None);
    }

    private Task<IReadOnlyList<InstrumentCoverageInternal>> ReadAsync(DateTimeOffset from, DateTimeOffset to, Guid[] instrumentIds, params Exchange[] exchanges)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetCandleHistoryCoverageQuery(instrumentIds, exchanges, new CandleHistoryWindow(CandleInterval.OneHour, from, to, Now)));
    }

    [Test]
    public async Task The_covered_parts_come_cut_to_the_window_by_instrument_id_and_an_instrument_without_any_gets_none()
    {
        await StoreAsync(Btc.Id, new MinuteRange(Day.AddHours(8), Day.AddHours(10)), new MinuteRange(Day.AddHours(8), Day.AddHours(10)));
        await StoreAsync(Btc.Id, new MinuteRange(Day.AddHours(10).AddMinutes(30), Day.AddHours(13)), new MinuteRange(Day.AddHours(10).AddMinutes(30), Day.AddHours(13)));

        IReadOnlyList<InstrumentCoverageInternal> coverage = await ReadAsync(Day.AddHours(9), Day.AddHours(12), [Btc.Id, Share.Id]);

        Assert.That(
            coverage,
            Is.EqualTo(
                new[]
                {
                    new InstrumentCoverageInternal(
                        Btc.Id,
                        [
                            new TimeRangeInternal(Day.AddHours(9), Day.AddHours(10)),
                            new TimeRangeInternal(Day.AddHours(10).AddMinutes(30), Day.AddHours(12))
                        ]),
                    new InstrumentCoverageInternal(Share.Id, [])
                }).Using(new CoverageComparer()));
    }

    [Test]
    public async Task A_bybit_day_whose_buy_volumes_are_not_settled_is_not_covered()
    {
        await StoreAsync(Btc.Id, new MinuteRange(Today.AddDays(-2), Today), settled: new MinuteRange(Today.AddDays(-2), Today.AddDays(-1)));

        IReadOnlyList<InstrumentCoverageInternal> coverage = await ReadAsync(Today.AddDays(-2), Today, [Btc.Id]);

        Assert.That(coverage.Single().Ranges, Is.EqualTo(new[] { new TimeRangeInternal(Today.AddDays(-2), Today.AddDays(-1)) }));
    }

    [Test]
    public async Task A_share_is_covered_by_its_loaded_minutes_with_nothing_settled()
    {
        await StoreAsync(Share.Id, new MinuteRange(Day, Day.AddHours(10)));

        IReadOnlyList<InstrumentCoverageInternal> coverage = await ReadAsync(Day, Day.AddDays(1), [Share.Id]);

        Assert.That(coverage.Single().Ranges, Is.EqualTo(new[] { new TimeRangeInternal(Day, Day.AddHours(10)) }));
    }

    [Test]
    public async Task Without_instruments_named_the_listed_ones_of_the_exchanges_named_are_read()
    {
        Assert.Multiple(async () =>
        {
            Assert.That((await ReadAsync(Day, Today, [], Exchange.Bybit)).Select(item => item.InstrumentId), Is.EqualTo(new[] { Btc.Id }));
            Assert.That((await ReadAsync(Day, Today, [], Exchange.TInvest)).Select(item => item.InstrumentId), Is.EqualTo(new[] { Share.Id }));
            Assert.That((await ReadAsync(Day, Today, [])).Select(item => item.InstrumentId), Is.EqualTo(new[] { Btc.Id, Share.Id }));
        });
    }

    [Test]
    public async Task A_delisted_instrument_is_left_out_even_when_named()
    {
        await StoreAsync(DelistedId, new MinuteRange(Day, Day.AddHours(1)), new MinuteRange(Day, Day.AddHours(1)));

        IReadOnlyList<InstrumentCoverageInternal> coverage = await ReadAsync(Day, Today, [Btc.Id, DelistedId]);

        Assert.That(coverage.Select(item => item.InstrumentId), Is.EqualTo(new[] { Btc.Id }));
    }

    [Test]
    public void An_instrument_not_in_the_catalog_fails()
    {
        Assert.That(() => ReadAsync(Day, Today, [Btc.Id, Guid.Parse("0193a1f0-0000-7000-8000-0000000000ff")]), Throws.TypeOf<InstrumentNotFoundException>());
    }

    // A record compares its list of ranges by reference; the coverage of two reads is the same when its ranges are.
    private sealed class CoverageComparer : IEqualityComparer<InstrumentCoverageInternal>
    {
        public bool Equals(InstrumentCoverageInternal? x, InstrumentCoverageInternal? y)
        {
            return x is not null && y is not null && x.InstrumentId == y.InstrumentId && x.Ranges.SequenceEqual(y.Ranges);
        }

        public int GetHashCode(InstrumentCoverageInternal obj)
        {
            return obj.InstrumentId.GetHashCode();
        }
    }
}