using MediatR;

using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;

namespace ProfitFox.MarketData.Application.Commands.Instruments.Sync;

/// <summary>
///     Brings the catalog of a category in line with the list of the exchange as of <see cref="RequestedAt" />: new instruments are first seen
///     then, gone ones are delisted then.
/// </summary>
public sealed record SyncInstrumentsCommand(BybitCategory Category, DateTimeOffset RequestedAt) : IRequest;