using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Tests.Integration.Infrastructure.Persistence.Queries.Handlers.Liquidations;

[TestFixture]
public class GetLiquidationsQueryHandlerTests
{
    [SetUp]
    public async Task SetUp()
    {
        await IntegrationEnvironment.ResetDatabase();
        _services = IntegrationEnvironment.Services();
        _scope = _services.CreateAsyncScope();
        _liquidations = _scope.ServiceProvider.GetRequiredService<ILiquidationRepository>();
        await _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().SaveAsync([Btc, Eth], CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    private static readonly DateTimeOffset Hour = new DateTimeOffset(year: 2026, month: 11, day: 14, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly Instrument Btc = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear());

    private static readonly Instrument Eth = InstrumentGenerator.Generate(BybitListedInstrumentGenerator.GenerateLinear("ETH"));

    private static readonly LiquidationSumInternal Nothing = new LiquidationSumInternal(Volume: 0m, Turnover: 0m, Count: 0);

    private ServiceProvider _services = null!;
    private AsyncServiceScope _scope;
    private ILiquidationRepository _liquidations = null!;

    private Task<LiquidationMinutesInternal> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to)
    {
        return _scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetLiquidationsQuery(instrumentId, new PointWindow(from, to)), CancellationToken.None);
    }

    // A stored minute of the instrument for every minute from the hour on: none heard, so each has sums of nothing and is not valid.
    private Task StoreMinutesAsync(int count)
    {
        return _liquidations.InvalidateAsync([Btc.Id], Hour, Hour.AddMinutes(count - 1), CancellationToken.None);
    }

    [Test]
    public async Task A_window_of_1000_minutes_gives_them_all_and_nothing_to_continue_from()
    {
        await StoreMinutesAsync(count: 1000);

        LiquidationMinutesInternal minutes = await ReadAsync(Btc.Id, Hour, Hour.AddDays(1));

        Assert.Multiple(() =>
        {
            Assert.That(minutes.Minutes, Has.Count.EqualTo(1000));
            Assert.That(minutes.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task A_window_of_more_than_1000_minutes_is_cut_and_tells_where_the_rest_starts()
    {
        await StoreMinutesAsync(count: 1001);

        LiquidationMinutesInternal first = await ReadAsync(Btc.Id, Hour, Hour.AddDays(1));
        LiquidationMinutesInternal rest = await ReadAsync(Btc.Id, first.ContinueFrom!.Value, Hour.AddDays(1));

        Assert.Multiple(() =>
        {
            Assert.That(first.Minutes, Has.Count.EqualTo(1000));
            Assert.That(first.Minutes[0].Time, Is.EqualTo(Hour));
            Assert.That(first.Minutes[^1].Time, Is.EqualTo(Hour.AddMinutes(999)));
            Assert.That(first.ContinueFrom, Is.EqualTo(Hour.AddMinutes(1000)));
            Assert.That(rest.Minutes, Is.EqualTo(new[] { new LiquidationMinuteInternal(Hour.AddMinutes(1000), Nothing, Nothing, IsValid: false) }));
            Assert.That(rest.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_not_found()
    {
        Assert.That(() => ReadAsync(Guid.NewGuid(), Hour, Hour.AddHours(1)), Throws.InstanceOf<InstrumentNotFoundException>());
    }

    [Test]
    public async Task An_instrument_with_no_minutes_in_the_window_has_an_empty_answer()
    {
        await _liquidations.AddAsync([LiquidationMinuteGenerator.Generate(Btc.Id, Hour.AddHours(2))], CancellationToken.None);

        LiquidationMinutesInternal minutes = await ReadAsync(Btc.Id, Hour, Hour.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(minutes.Minutes, Is.Empty);
            Assert.That(minutes.ContinueFrom, Is.Null);
        });
    }

    [Test]
    public async Task The_minutes_of_the_window_come_oldest_first_as_they_are_stored_each_with_its_mark()
    {
        // A minute before the window, one at its end and one of another instrument are not of it. The minutes between the stored ones
        // had no liquidation, and nothing stands in for them.
        await _liquidations.AddAsync(
            [
                new LiquidationMinute(
                    Btc.Id,
                    Hour.AddMinutes(25),
                    new LiquidationSum(volume: 0.75m, turnover: 76125.5m, count: 3),
                    new LiquidationSum(volume: 0.001m, turnover: 101.337m, count: 1)),
                LiquidationMinuteGenerator.Generate(Btc.Id, Hour.AddMinutes(-1)),
                LiquidationMinuteGenerator.Generate(Btc.Id, Hour),
                LiquidationMinuteGenerator.Generate(Btc.Id, Hour.AddMinutes(50)),
                LiquidationMinuteGenerator.Generate(Eth.Id, Hour.AddMinutes(5))
            ],
            CancellationToken.None);
        await _liquidations.InvalidateAsync([Btc.Id], Hour, Hour.AddMinutes(1), CancellationToken.None);

        LiquidationMinutesInternal minutes = await ReadAsync(Btc.Id, Hour, Hour.AddMinutes(50));

        Assert.Multiple(() =>
        {
            Assert.That(
                minutes.Minutes,
                Is.EqualTo(
                    new[]
                    {
                        new LiquidationMinuteInternal(Hour, new LiquidationSumInternal(Volume: 2m, Turnover: 10m, Count: 1), Nothing, IsValid: false),
                        new LiquidationMinuteInternal(Hour.AddMinutes(1), Nothing, Nothing, IsValid: false),
                        new LiquidationMinuteInternal(
                            Hour.AddMinutes(25),
                            new LiquidationSumInternal(Volume: 0.75m, Turnover: 76125.5m, Count: 3),
                            new LiquidationSumInternal(Volume: 0.001m, Turnover: 101.337m, Count: 1),
                            IsValid: true)
                    }));
            Assert.That(minutes.ContinueFrom, Is.Null);
        });
    }
}