using Grpc.Core;

namespace ProfitFox.MarketData.Tests.Integration.Grpc;

/// <summary>
///     Answers every unary gRPC call with the answer prepared for its method and records what was asked, so no test reaches a real
///     exchange. A method without an answer fails with <see cref="StatusCode.Unimplemented" />; streams are not answered.
/// </summary>
internal sealed class StubGrpcInvoker : CallInvoker
{
    private readonly Dictionary<string, Func<object>> _answers = [];

    /// <summary>
    ///     Every call, in order.
    /// </summary>
    public List<StubGrpcCall> Calls { get; } = [];

    /// <summary>
    ///     Answers <paramref name="method" /> (its name, such as <c>Shares</c>) with <paramref name="response" />.
    /// </summary>
    public StubGrpcInvoker Respond(string method, object response)
    {
        _answers[method] = () => response;

        return this;
    }

    /// <summary>
    ///     Fails <paramref name="method" /> with <paramref name="status" />, as the server does.
    /// </summary>
    public StubGrpcInvoker Fail(string method, StatusCode status)
    {
        _answers[method] = () => throw new RpcException(new Status(status, $"{method} failed."));

        return this;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        Calls.Add(new StubGrpcCall(method.Name, request, options.Headers ?? []));

        return new AsyncUnaryCall<TResponse>(Answer<TResponse>(method.Name), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        throw new NotSupportedException("The stub answers asynchronous calls only.");
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request)
    {
        throw new NotSupportedException("The stub answers no streams.");
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options)
    {
        throw new NotSupportedException("The stub answers no streams.");
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options)
    {
        throw new NotSupportedException("The stub answers no streams.");
    }

    private Task<TResponse> Answer<TResponse>(string method)
    {
        if (!_answers.TryGetValue(method, out Func<object>? answer))
        {
            return Task.FromException<TResponse>(new RpcException(new Status(StatusCode.Unimplemented, $"No answer prepared for {method}.")));
        }

        try
        {
            return Task.FromResult((TResponse)answer());
        }
        catch (RpcException exception)
        {
            return Task.FromException<TResponse>(exception);
        }
    }
}