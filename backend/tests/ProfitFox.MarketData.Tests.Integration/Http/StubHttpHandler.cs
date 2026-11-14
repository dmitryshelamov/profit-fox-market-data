using System.Net;

namespace ProfitFox.MarketData.Tests.Integration.Http;

/// <summary>
///     Answers every HTTP request from a queue of prepared answers, a JSON body or the bytes of a file with its status, and records what
///     was asked, so no test reaches a real exchange.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, Func<HttpContent> Body)> _answers = new Queue<(HttpStatusCode Status, Func<HttpContent> Body)>();

    /// <summary>
    ///     The address of every request, in order.
    /// </summary>
    public List<Uri> Requests { get; } = [];

    /// <summary>
    ///     The client every request named in its <c>User-Agent</c>, in order; empty for a request that named none.
    /// </summary>
    public List<string> Clients { get; } = [];

    public StubHttpHandler RespondJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _answers.Enqueue((status, () => new StringContent(json)));

        return this;
    }

    public StubHttpHandler RespondBytes(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _answers.Enqueue((status, () => new ByteArrayContent(body)));

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Clients.Add(request.Headers.UserAgent.ToString());

        (HttpStatusCode status, Func<HttpContent> body) = _answers.TryDequeue(out (HttpStatusCode Status, Func<HttpContent> Body) next)
            ? next
            : throw new InvalidOperationException($"No answer prepared for {request.RequestUri}.");

        return Task.FromResult(new HttpResponseMessage(status) { Content = body() });
    }
}