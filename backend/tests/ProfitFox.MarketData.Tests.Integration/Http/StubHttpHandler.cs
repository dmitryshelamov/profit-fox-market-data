using System.Net;

namespace ProfitFox.MarketData.Tests.Integration.Http;

/// <summary>
///     Answers every HTTP request from a queue of prepared JSON bodies and records what was asked, so no test reaches a real exchange.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<string> _bodies = new Queue<string>();

    /// <summary>
    ///     The address of every request, in order.
    /// </summary>
    public List<Uri> Requests { get; } = [];

    public StubHttpHandler RespondJson(string json)
    {
        _bodies.Enqueue(json);

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        string body = _bodies.TryDequeue(out string? next)
            ? next
            : throw new InvalidOperationException($"No answer prepared for {request.RequestUri}.");

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}