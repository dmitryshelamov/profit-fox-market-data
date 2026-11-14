using MediatR;

using ProfitFox.MarketData.Application.Commands.Base;
using ProfitFox.MarketData.Application.Queries.Base;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Transactions;

namespace ProfitFox.MarketData.Infrastructure.Mediatr.Behaviors;

/// <summary>
///     Runs a command in one transaction, so it saves everything or nothing. A query runs without one, and so does a command that asks
///     an exchange (<see cref="ICommandWithoutTransaction{TResponse}" />): no transaction stays open while the exchange answers.
/// </summary>
internal sealed class PostgresTransactionBehavior<TRequest, TResponse>(PostgresTransactionExecutor transactions)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IQuery<TResponse> or ICommandWithoutTransaction<TResponse>)
        {
            return await next(cancellationToken);
        }

        return await transactions.ExecuteAsync(() => next(cancellationToken), cancellationToken);
    }
}