using MediatR;

using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadBuyVolumes;

/// <summary>
///     Settles the buy volumes of the newest whole UTC days of <see cref="Range" /> that are not settled yet in the series of an
///     instrument of Bybit: the candles of a day that have none get them from the archive of trades of that day. At most one archive is
///     read a command; days that need none are settled on the way to it. A caller sends it again while it gives
///     <see cref="LoadBuyVolumesResult.MoreLeft" />.
/// </summary>
public sealed record LoadBuyVolumesCommand(Guid InstrumentId, MinuteRange Range, DateTimeOffset RequestedAt) : IRequest<LoadBuyVolumesResult>;