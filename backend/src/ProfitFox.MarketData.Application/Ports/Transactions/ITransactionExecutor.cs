namespace ProfitFox.MarketData.Application.Ports.Transactions;

/// <summary>
///     Makes several saves one: for a handler that must save through more than one call of a repository together. A handler asks it only
///     around the saves, never around a request to an exchange.
/// </summary>
public interface ITransactionExecutor
{
    /// <summary>
    ///     Runs <paramref name="action" /> in one transaction of storage: what its repositories save is saved together, or not at all when
    ///     it throws.
    /// </summary>
    Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken);
}