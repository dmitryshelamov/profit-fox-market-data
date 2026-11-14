using System.Threading.Channels;

using Grpc.Core;

namespace ProfitFox.MarketData.Tests.Integration.Grpc;

/// <summary>
///     A two-way gRPC stream <see cref="StubGrpcInvoker" /> opened for the code under test: it records what the code sent and tells it
///     what a test has the server say, or breaks or ends as a server may.
/// </summary>
internal sealed class StubGrpcStream<TRequest, TResponse>(string method, Metadata headers) : IStubGrpcStream, IClientStreamWriter<TRequest>, IAsyncStreamReader<TResponse>
    where TRequest : class
    where TResponse : class
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly Channel<TResponse> _responses = Channel.CreateUnbounded<TResponse>();

    private readonly Lock _lock = new Lock();

    private readonly List<object> _sent = [];

    private TaskCompletionSource _read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Method { get; } = method;

    public Metadata Headers { get; } = headers;

    public bool Closed { get; private set; }

    public WriteOptions? WriteOptions { get; set; }

    public TResponse Current { get; private set; } = null!;

    public IReadOnlyList<object> Sent
    {
        get
        {
            lock (_lock)
            {
                return [.. _sent];
            }
        }
    }

    public void Tell(object response)
    {
        // Told under the lock the reader checks under: it never takes the message as read between the two steps.
        lock (_lock)
        {
            _read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _responses.Writer.TryWrite((TResponse)response);
        }
    }

    public void Break(StatusCode status)
    {
        _responses.Writer.TryComplete(new RpcException(new Status(status, $"{Method} broke.")));
    }

    public void End()
    {
        _responses.Writer.TryComplete();
    }

    public async Task SentAsync(int count)
    {
        DateTime until = DateTime.UtcNow + Patience;

        while (Sent.Count < count)
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"The code sent {Sent.Count} messages on {Method}, not {count}.");
            }

            await Task.Delay(10);
        }
    }

    public async Task ReadAsync()
    {
        Task read;

        lock (_lock)
        {
            read = _read.Task;
        }

        await read.WaitAsync(Patience);
    }

    public Task WriteAsync(TRequest message)
    {
        lock (_lock)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task WriteAsync(TRequest message, CancellationToken cancellationToken)
    {
        return WriteAsync(message);
    }

    public Task CompleteAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        // Asking for the next message with none waiting means the code is done with all it was told.
        lock (_lock)
        {
            if (!_responses.Reader.TryPeek(out _))
            {
                _read.TrySetResult();
            }
        }

        if (!await _responses.Reader.WaitToReadAsync(cancellationToken))
        {
            return false;
        }

        _responses.Reader.TryRead(out TResponse? response);
        Current = response!;

        return true;
    }

    public void Close()
    {
        Closed = true;
    }
}