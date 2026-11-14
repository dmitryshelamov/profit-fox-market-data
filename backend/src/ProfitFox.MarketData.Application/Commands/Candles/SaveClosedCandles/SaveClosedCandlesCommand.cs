using MediatR;

namespace ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;

/// <summary>
///     Saves the candles of minutes that have just closed, of any instruments, into their series and covers those minutes, all together,
///     and tells the saved ones to the feed of closed candles. Of a minute of an instrument told more than once, the last told with a
///     buy volume is saved, or the last told when none has one. A series whose loading has not started takes nothing: its candles are
///     left out and their minutes are left to the loading of candles.
/// </summary>
public sealed record SaveClosedCandlesCommand(IReadOnlyCollection<ClosedCandle> Candles) : IRequest<SaveClosedCandlesResult>;