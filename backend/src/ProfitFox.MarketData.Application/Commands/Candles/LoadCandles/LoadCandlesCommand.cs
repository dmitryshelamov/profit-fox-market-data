using MediatR;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;

/// <summary>
///     Loads one page of the minutes of <see cref="Range" /> the series of the instrument does not cover, the newest first: one request to
///     its exchange, saved at once. Only minutes closed by <see cref="RequestedAt" /> are loaded. With <see cref="WholeHistory" /> the
///     range may reach back before the instrument traded: once only the oldest part of the range is left, the first page the exchange
///     gives no candle for is taken for the time before trading started, and the range is covered with no candles back to its start. A
///     caller sends it again while it gives <see cref="LoadCandlesResult.MoreLeft" />.
/// </summary>
public sealed record LoadCandlesCommand(Guid InstrumentId, MinuteRange Range, DateTimeOffset RequestedAt, bool WholeHistory = false)
    : IRequest<LoadCandlesResult>;