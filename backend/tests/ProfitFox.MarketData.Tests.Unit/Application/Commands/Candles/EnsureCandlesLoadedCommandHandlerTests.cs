using MediatR;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.MarketData.Application.Commands.Candles.EnsureCandlesLoaded;
using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entities.Instruments;

namespace ProfitFox.MarketData.Tests.Unit.Application.Commands.Candles;

[TestFixture]
public class EnsureCandlesLoadedCommandHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _instruments = Substitute.For<IInstrumentRepository>();
        _sender = Substitute.For<ISender>();
        _queue = Substitute.For<ICandleLoadQueue>();
        _instruments.GetAsync(Btc.Id, Arg.Any<CancellationToken>()).Returns(Btc);

        _handler = new EnsureCandlesLoadedCommandHandler(_instruments, _sender, _queue);
    }

    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 11, day: 12, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private static readonly MinuteRange Range = new MinuteRange(Now.AddHours(-5), Now);

    private static readonly Instrument Btc = InstrumentGenerator.Generate();

    private IInstrumentRepository _instruments = null!;
    private ISender _sender = null!;
    private ICandleLoadQueue _queue = null!;
    private EnsureCandlesLoadedCommandHandler _handler = null!;

    private Task<CandlesReadiness> Ensure(int maxExchangeRequests = 3)
    {
        return _handler.Handle(new EnsureCandlesLoadedCommand(Btc.Id, Range, Now, maxExchangeRequests), CancellationToken.None);
    }

    private void Pages(params LoadCandlesResult[] results)
    {
        _sender.Send(new LoadCandlesCommand(Btc.Id, Range, Now), Arg.Any<CancellationToken>()).Returns(results[0], results[1..]);
    }

    [Test]
    public async Task A_range_that_needs_more_requests_than_the_read_may_wait_for_is_loading_and_queued()
    {
        Pages(LoadCandlesResult.MoreLeft, LoadCandlesResult.MoreLeft, LoadCandlesResult.MoreLeft, LoadCandlesResult.NothingLeft);

        CandlesReadiness readiness = await Ensure(maxExchangeRequests: 3);

        Assert.Multiple(async () =>
        {
            Assert.That(readiness, Is.EqualTo(CandlesReadiness.Loading));
            await _sender.Received(3).Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>());
            _queue.Received(1).Enqueue(Btc.Id, Range);
        });
    }

    [Test]
    public void An_instrument_the_catalog_does_not_have_is_rejected_before_any_page()
    {
        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InstrumentNotFoundException>(() => _handler.Handle(
                new EnsureCandlesLoadedCommand(Guid.NewGuid(), Range, Now, MaxExchangeRequests: 3),
                CancellationToken.None));
            Assert.That(_sender.ReceivedCalls(), Is.Empty);
        });
    }

    [Test]
    public async Task Once_nothing_is_left_the_range_is_ready_and_no_more_pages_are_sent()
    {
        Pages(LoadCandlesResult.MoreLeft, LoadCandlesResult.NothingLeft);

        CandlesReadiness readiness = await Ensure(maxExchangeRequests: 3);

        Assert.Multiple(async () =>
        {
            Assert.That(readiness, Is.EqualTo(CandlesReadiness.Ready));
            await _sender.Received(2).Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>());
            _queue.DidNotReceiveWithAnyArgs().Enqueue(default, default!);
        });
    }

    [Test]
    public async Task While_the_exchange_holds_or_fails_the_requests_the_range_is_loading_and_queued()
    {
        _sender.Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangePausedException("Bybit limited the requests.", Now.AddSeconds(30)));
        CandlesReadiness paused = await Ensure();

        _sender.Send(Arg.Any<LoadCandlesCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TransientExchangeException("Bybit answered 502."));
        CandlesReadiness unavailable = await Ensure();

        Assert.Multiple(() =>
        {
            Assert.That(paused, Is.EqualTo(CandlesReadiness.Loading));
            Assert.That(unavailable, Is.EqualTo(CandlesReadiness.Loading));
            _queue.Received(2).Enqueue(Btc.Id, Range);
        });
    }
}