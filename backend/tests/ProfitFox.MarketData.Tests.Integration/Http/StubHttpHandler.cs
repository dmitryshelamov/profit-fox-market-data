using System.Net;

namespace ProfitFox.MarketData.Tests.Integration.Http;

/// <summary>
///     Answers every HTTP request from a queue of prepared answers, a JSON body or the bytes of a file with its status, a failure of the
///     connection or no answer at all, and records what was asked, so no test reaches a real exchange.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    // An answer with a failure throws it; one without a body never comes.
    private readonly Queue<(HttpStatusCode Status, Func<HttpContent>? Body, Exception? Failure)> _answers =
        new Queue<(HttpStatusCode Status, Func<HttpContent>? Body, Exception? Failure)>();

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
        _answers.Enqueue((status, () => new StringContent(json), null));

        return this;
    }

    public StubHttpHandler RespondBytes(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _answers.Enqueue((status, () => new ByteArrayContent(body), null));

        return this;
    }

    /// <summary>
    ///     The next request gets no answer at all: it ends only when its caller gives it up.
    /// </summary>
    public StubHttpHandler NeverRespond()
    {
        _answers.Enqueue((HttpStatusCode.OK, null, null));

        return this;
    }

    /// <summary>
    ///     The next request fails with <paramref name="failure" />, as a broken connection or a timeout does.
    /// </summary>
    public StubHttpHandler Fail(Exception failure)
    {
        _answers.Enqueue((HttpStatusCode.OK, null, failure));

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Clients.Add(request.Headers.UserAgent.ToString());

        (HttpStatusCode status, Func<HttpContent>? body, Exception? failure) =
            _answers.TryDequeue(out (HttpStatusCode Status, Func<HttpContent>? Body, Exception? Failure) next)
                ? next
                : throw new InvalidOperationException($"No answer prepared for {request.RequestUri}.");

        if (failure is not null)
        {
            throw failure;
        }

        // An answer that never comes: the wait ends only with the request, when its caller gives it up.
        if (body is null)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return new HttpResponseMessage(status) { Content = body?.Invoke() };
    }
}