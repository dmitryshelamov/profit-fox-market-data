using MediatR;

namespace ProfitFox.MarketData.Application.Queries.Base;

/// <summary>
///     A request that only reads: it runs outside the transaction of a command.
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;