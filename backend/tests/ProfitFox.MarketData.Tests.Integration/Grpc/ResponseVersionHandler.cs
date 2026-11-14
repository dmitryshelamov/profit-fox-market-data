namespace ProfitFox.MarketData.Tests.Integration.Grpc;

/// <summary>
///     Lets a gRPC client call the app in memory: the test server answers with no HTTP version of its own, and the client refuses an
///     answer that is not HTTP/2, so the answer takes the version of its request.
/// </summary>
internal sealed class ResponseVersionHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        response.Version = request.Version;

        return response;
    }
}