using MediatR;

namespace ProfitFox.MarketData.Application.Commands.Instruments.Base;

/// <summary>
///     Brings the instruments of one market in line with the list of its exchange as of <see cref="RequestedAt" />: new instruments are first
///     seen then, gone ones are delisted then.
/// </summary>
public abstract record BaseSyncInstrumentsCommand(DateTimeOffset RequestedAt) : IRequest;