namespace ProfitFox.MarketData.Application.Ports.Feeds;

/// <summary>
///     Market data of one kind as it comes, for whoever follows it live: each item is published once it is saved, so a subscriber
///     never hears of one storage does not hold. The feed keeps nothing: a subscriber gets what is published while it is subscribed,
///     and reads what it missed from storage.
/// </summary>
public interface ILiveFeed<T>
{
    /// <summary>
    ///     Tells the items to every subscriber, in the order given; it never waits for a subscriber.
    /// </summary>
    void Publish(IReadOnlyCollection<T> items);

    /// <summary>
    ///     Starts to gather what is published from now on for a new subscriber, until the subscription is disposed.
    /// </summary>
    ILiveSubscription<T> Subscribe();
}