using System.Net;

namespace ProfitFox.MarketData.Tests.Integration.Http;

/// <summary>
///     Answers every HTTP request from a queue of prepared answers, a JSON body with its status, and records what was asked, so no test
///     reaches a real exchange.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _answers = new Queue<(HttpStatusCode Status, string Body)>();

    /// <summary>
    ///     The address of every request, in order.
    /// </summary>
    public List<Uri> Requests { get; } = [];

    public StubHttpHandler RespondJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _answers.Enqueue((status, json));

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        (HttpStatusCode status, string body) = _answers.TryDequeue(out (HttpStatusCode Status, string Body) next)
            ? next
            : throw new InvalidOperationException($"No answer prepared for {request.RequestUri}.");

        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}