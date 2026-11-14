using MediatR;

namespace ProfitFox.MarketData.Application.Commands.Base;

/// <summary>
///     A command that runs outside the transaction a command gets: it asks an exchange, which no open transaction should wait for, and
///     saves through a call of a repository that is whole by itself.
/// </summary>
public interface ICommandWithoutTransaction<out TResponse> : IRequest<TResponse>;