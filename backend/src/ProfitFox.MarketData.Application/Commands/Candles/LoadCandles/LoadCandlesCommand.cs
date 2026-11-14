using ProfitFox.MarketData.Application.Commands.Base;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;

/// <summary>
///     Loads one page of the minutes of <see cref="Range" /> the series of the instrument does not cover, the newest first: one request to
///     its exchange, saved at once. Only minutes closed by <see cref="RequestedAt" /> are loaded. A caller sends it again while it gives
///     <see cref="LoadCandlesResult.MoreLeft" />.
/// </summary>
public sealed record LoadCandlesCommand(Guid InstrumentId, MinuteRange Range, DateTimeOffset RequestedAt)
    : ICommandWithoutTransaction<LoadCandlesResult>;