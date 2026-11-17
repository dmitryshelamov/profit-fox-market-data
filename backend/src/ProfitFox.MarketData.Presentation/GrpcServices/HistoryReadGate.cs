using Microsoft.Extensions.Options;

using ProfitFox.MarketData.Presentation.Settings;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     The places of the calls of history that read storage at once, <see cref="HistoryReadSettings.MaxConcurrentReads" /> of them: a
///     call over them waits its turn, first come first served, until a place frees or its caller gives up.
/// </summary>
public sealed class HistoryReadGate(IOptionsMonitor<HistoryReadSettings> settings)
{
    private readonly Lock _lock = new Lock();
    private readonly LinkedList<TaskCompletionSource> _waiting = new LinkedList<TaskCompletionSource>();
    private int _reading;

    /// <summary>
    ///     Waits for a place and takes it; disposing what it gives frees the place.
    /// </summary>
    /// <exception cref="OperationCanceledException">The caller gave up while it waited; it took no place.</exception>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LinkedListNode<TaskCompletionSource> node;

        lock (_lock)
        {
            node = _waiting.AddLast(turn);
            HandOutPlaces();
        }

        // A waiter that gives up leaves the queue, unless its place was handed to it first: then it has the place and frees it.
        await using (cancellationToken.Register(() => GiveUp(node, cancellationToken)))
        {
            await turn.Task;
        }

        return new Place(this);
    }

    private void GiveUp(LinkedListNode<TaskCompletionSource> node, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (node.List is null)
            {
                return;
            }

            _waiting.Remove(node);
        }

        node.Value.TrySetCanceled(cancellationToken);
    }

    private void Leave()
    {
        lock (_lock)
        {
            _reading--;
            HandOutPlaces();
        }
    }

    // The limit is read now, so a raised one lets in more of those waiting and a lowered one lets in none until enough places free.
    private void HandOutPlaces()
    {
        while (_waiting.First is { } first && _reading < settings.CurrentValue.MaxConcurrentReads)
        {
            _waiting.RemoveFirst();
            _reading++;
            first.Value.TrySetResult();
        }
    }

    /// <summary>
    ///     A place taken; freed once, however many times it is disposed.
    /// </summary>
    private sealed class Place(HistoryReadGate gate) : IDisposable
    {
        private int _freed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _freed, 1) == 0)
            {
                gate.Leave();
            }
        }
    }
}