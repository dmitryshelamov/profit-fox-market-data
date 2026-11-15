using Grpc.Core;
using Grpc.Core.Interceptors;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     Counts every unary call to T-Invest in <see cref="TInvestMetrics" /> once it ends and reads the limit headers of its answer:
///     <c>RESOURCE_EXHAUSTED</c> is <c>rate_limited</c>, another failure <c>error</c>. A call its caller gives up is not counted: the
///     channel ends it with an <see cref="OperationCanceledException" />, not a failure of the exchange. Streams pass uncounted.
/// </summary>
internal sealed class TInvestMetricsInterceptor(TInvestMetrics metrics) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        string group = TInvestMetrics.GroupOf(context.Method.ServiceName);
        AsyncUnaryCall<TResponse> call = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(CountAsync(call, group, context.Method.Name), call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);
    }

    private async Task<TResponse> CountAsync<TResponse>(AsyncUnaryCall<TResponse> call, string group, string method)
    {
        TResponse response;

        try
        {
            response = await call.ResponseAsync;
        }
        catch (RpcException exception)
        {
            metrics.Count(group, method, exception.StatusCode == StatusCode.ResourceExhausted ? "rate_limited" : "error");

            throw;
        }

        metrics.Observe(group, await call.ResponseHeadersAsync);
        metrics.Count(group, method, "ok");

        return response;
    }
}