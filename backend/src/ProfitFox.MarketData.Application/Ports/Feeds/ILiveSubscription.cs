using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Application.Ports.Feeds;

/// <summary>
///     What one subscriber of a live feed has waiting: the items published since it subscribed, in the order they were published. A
///     subscriber that reads too slowly is cut off instead of holding back the publisher; what a feed may leave out before that, its
///     port says (<see cref="ILiveFeed{T}" /> leaves out nothing).
/// </summary>
public interface ILiveSubscription<T> : IDisposable
{
    /// <summary>
    ///     The items as they come, the waiting ones first; it ends once the subscription is disposed and the waiting ones are read, or
    ///     when cancelled or cut off.
    /// </summary>
    /// <exception cref="SubscriberFellBehindException">
    ///     More items waited than a subscription holds: the subscriber is cut off after the ones that fitted.
    /// </exception>
    IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken);
}