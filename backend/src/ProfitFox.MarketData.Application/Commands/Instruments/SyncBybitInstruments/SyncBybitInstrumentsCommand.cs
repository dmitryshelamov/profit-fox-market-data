using ProfitFox.MarketData.Application.Commands.Instruments.Base;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Commands.Instruments.SyncBybitInstruments;

/// <summary>
///     Brings the instruments of a category of Bybit in line with its list as of <see cref="BaseSyncInstrumentsCommand.RequestedAt" />.
/// </summary>
public sealed record SyncBybitInstrumentsCommand(BybitCategory Category, DateTimeOffset RequestedAt) : BaseSyncInstrumentsCommand(RequestedAt);