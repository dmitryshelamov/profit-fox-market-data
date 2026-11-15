using Grpc.Core;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Feeds;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     What a call of a feed of points does, whatever the kind of the points: it subscribes to the live ones, tells the stored ones the
///     caller asked to catch up on, then the live ones, and ends the way the contracts of the feeds say. A feed service tells only what
///     it reads from storage and how a point becomes a message.
/// </summary>
public sealed class FeedCall(IHostApplicationLifetime lifetime, TimeProvider time)
{
    /// <summary>
    ///     Serves the call until its caller or the app ends it.
    /// </summary>
    /// <exception cref="RpcException">
    ///     INVALID_ARGUMENT when <paramref name="since" /> is further back than <paramref name="maxCatchUpHours" />; RESOURCE_EXHAUSTED
    ///     when the caller fell behind the feed; UNAVAILABLE when the app is stopping.
    /// </exception>
    public async Task ServeAsync<TItem, TUpdate>(
        DateTimeOffset? since,
        int maxCatchUpHours,
        ILiveFeed<TItem> feed,
        Func<DateTimeOffset, CancellationToken, IAsyncEnumerable<TItem>> stored,
        Func<TItem, TUpdate> toUpdate,
        IServerStreamWriter<TUpdate> responseStream,
        ServerCallContext context)
    {
        if (since is not null && since < time.GetUtcNow().AddHours(-maxCatchUpHours))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The feed catches up on at most {maxCatchUpHours} hours; since is {since:O}."));
        }

        // Subscribed before storage is read: an item saved meanwhile waits in the subscription, so none is left out between the two.
        // It may come twice, from storage and live, which the caller takes as one: it replaces an item by instrument and time.
        using ILiveSubscription<TItem> live = feed.Subscribe();

        // The headers tell the caller that the subscription is in place: what is saved from now on reaches it.
        await context.WriteResponseHeadersAsync([]);

        // The call ends with the app as well as with its caller: the server waits for the open calls when it stops, and one only its
        // caller ends would hold the stop back until the server gives up.
        using var ended = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);

        try
        {
            if (since is not null)
            {
                await foreach (TItem item in stored(since.Value, ended.Token))
                {
                    await responseStream.WriteAsync(toUpdate(item), ended.Token);
                }
            }

            await foreach (TItem item in live.ReadAllAsync(ended.Token))
            {
                await responseStream.WriteAsync(toUpdate(item), ended.Token);
            }
        }
        catch (SubscriberFellBehindException exception)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, exception.Message));
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "The service is stopping."));
        }
    }
}