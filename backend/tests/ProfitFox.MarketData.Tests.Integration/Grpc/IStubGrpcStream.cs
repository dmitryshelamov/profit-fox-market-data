using Grpc.Core;

namespace ProfitFox.MarketData.Tests.Integration.Grpc;

/// <summary>
///     A stream of <see cref="StubGrpcInvoker" /> whatever its messages are.
/// </summary>
internal interface IStubGrpcStream
{
    /// <summary>
    ///     The name of the method, such as <c>MarketDataStream</c>.
    /// </summary>
    string Method { get; }

    Metadata Headers { get; }

    /// <summary>
    ///     What the code sent on the stream, in order.
    /// </summary>
    IReadOnlyList<object> Sent { get; }

    /// <summary>
    ///     Whether the code closed its call.
    /// </summary>
    bool Closed { get; }

    /// <summary>
    ///     Has the server say <paramref name="response" />.
    /// </summary>
    void Tell(object response);

    /// <summary>
    ///     Breaks the stream with <paramref name="status" />, as a lost connection does.
    /// </summary>
    void Break(StatusCode status);

    /// <summary>
    ///     Ends the stream as the server does when it is done.
    /// </summary>
    void End();

    /// <summary>
    ///     Waits until the code has sent <paramref name="count" /> messages; fails when it has not in five seconds.
    /// </summary>
    Task SentAsync(int count);

    /// <summary>
    ///     Waits until the code has taken everything it was told and asks for more; fails when it has not in five seconds.
    /// </summary>
    Task ReadAsync();
}