using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Presentation.Protos;

namespace ProfitFox.MarketData.Presentation.GrpcServices;

/// <summary>
///     What a call of history does, whatever the kind of its data: it waits for its place among the reads, tells the coverage of every
///     instrument, then the rows inside it, and ends the way the contracts of the feeds say. A feed service tells only what it asks and
///     how a row becomes a message.
/// </summary>
public sealed class HistoryCall(IHostApplicationLifetime lifetime, HistoryReadGate gate, TimeProvider time)
{
    /// <summary>
    ///     The time the window of a call is checked against: it ends at the start of its UTC day or before.
    /// </summary>
    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>
    ///     What a request asks, checked before the call waits for its place: a request the service cannot read waits for nothing.
    /// </summary>
    /// <exception cref="RpcException">
    ///     INVALID_ARGUMENT when <paramref name="ask" /> finds the request wrong: a window, an exchange or an id that is not one.
    /// </exception>
    public static T Checked<T>(Func<T> ask)
    {
        try
        {
            return ask();
        }
        catch (ArgumentException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }

    /// <summary>
    ///     The instruments a request names, by their ids in the catalog.
    /// </summary>
    /// <exception cref="ArgumentException">An id is not one.</exception>
    public static Guid[] Ids(IReadOnlyCollection<string> instrumentIds)
    {
        return
        [
            .. instrumentIds.Select(id => Guid.TryParse(id, out Guid parsed)
                ? parsed
                : throw new ArgumentException($"The instrument id {id} is not an id."))
                .Distinct()
        ];
    }

    /// <summary>
    ///     An end of the window of a request.
    /// </summary>
    /// <exception cref="ArgumentException">The request does not set it.</exception>
    public static DateTimeOffset Time(Timestamp? time, string name)
    {
        return time?.ToDateTimeOffset() ?? throw new ArgumentException($"The window must have its {name}.");
    }

    /// <summary>
    ///     The message of the coverage of an instrument, which every feed tells the same way.
    /// </summary>
    public static InstrumentCoverage ToMessage(InstrumentCoverageInternal coverage)
    {
        var message = new InstrumentCoverage { InstrumentId = coverage.InstrumentId.ToString() };
        message.Ranges.AddRange(
            coverage.Ranges.Select(range => new TimeRange { From = Timestamp.FromDateTimeOffset(range.From), Until = Timestamp.FromDateTimeOffset(range.Until) }));

        return message;
    }

    /// <summary>
    ///     Serves the call until all of it is told, its caller cancels it, or the app stops.
    /// </summary>
    /// <exception cref="RpcException">
    ///     NOT_FOUND when an instrument named is not in the catalog; UNAVAILABLE when the app is stopping or storage did not answer a
    ///     statement in time, which the caller reads again.
    /// </exception>
    public async Task ServeAsync<TRow, TItem>(
        Func<CancellationToken, Task<IReadOnlyList<InstrumentCoverageInternal>>> coverage,
        Func<IReadOnlyList<InstrumentCoverageInternal>, CancellationToken, IAsyncEnumerable<TRow>> rows,
        Func<InstrumentCoverageInternal, TItem> coverageToItem,
        Func<TRow, TItem> rowToItem,
        IServerStreamWriter<TItem> responseStream,
        ServerCallContext context)
    {
        // The call ends with the app as well as with its caller, while it waits for its place too: the server waits for the open calls
        // when it stops, and one only its caller ends would hold the stop back until the server gives up.
        using var ended = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);

        try
        {
            using IDisposable place = await gate.EnterAsync(ended.Token);

            // Read before the rows, and the rows only inside it: what the loading stores meanwhile is not told.
            IReadOnlyList<InstrumentCoverageInternal> covered = await coverage(ended.Token);

            foreach (InstrumentCoverageInternal instrument in covered)
            {
                await responseStream.WriteAsync(coverageToItem(instrument), ended.Token);
            }

            await foreach (TRow row in rows(covered, ended.Token).WithCancellation(ended.Token))
            {
                await responseStream.WriteAsync(rowToItem(row), ended.Token);
            }
        }
        catch (InstrumentNotFoundException exception)
        {
            throw new RpcException(new Status(StatusCode.NotFound, exception.Message));
        }
        catch (StorageTimeoutException exception)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, exception.Message));
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "The service is stopping."));
        }
    }
}