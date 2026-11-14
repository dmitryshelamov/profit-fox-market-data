using MediatR;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;

/// <summary>
///     Loads one page of the minutes of <see cref="Range" /> the series of the instrument does not cover, the newest first: one request to
///     its exchange, saved at once. Only minutes closed by <see cref="RequestedAt" /> are loaded. Gives whether minutes of
///     <see cref="Range" /> are still left to load, so a caller sends it again until it gives false.
/// </summary>
public sealed record LoadCandlesCommand(Guid InstrumentId, MinuteRange Range, DateTimeOffset RequestedAt) : IRequest<bool>;