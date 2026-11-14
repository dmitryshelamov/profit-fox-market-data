using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProfitFox.MarketData.Tests.Integration.Sockets;

/// <summary>
///     The public streams of Bybit v5 on a local port, <c>/v5/public/spot</c> and <c>/v5/public/linear</c>. It answers <c>subscribe</c>,
///     <c>unsubscribe</c> and <c>ping</c> as Bybit does, pushes the messages a test gives it and tells what clients subscribed to.
/// </summary>
internal sealed class FakeBybitSocketServer : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly WebApplication _app;

    private readonly ConcurrentDictionary<WebSocket, Connection> _connections = new ConcurrentDictionary<WebSocket, Connection>();

    /// <summary>
    ///     Like <c>ws://127.0.0.1:5297</c>.
    /// </summary>
    public string Address { get; private set; } = "";

    /// <summary>
    ///     Topics a subscription to is refused, as Bybit refuses one it does not serve.
    /// </summary>
    public ConcurrentDictionary<string, bool> RefusedTopics { get; } = new ConcurrentDictionary<string, bool>();

    /// <summary>
    ///     The topics of each subscribe request clients sent, in order, on any path.
    /// </summary>
    public ConcurrentQueue<string[]> SubscribeRequests { get; } = new ConcurrentQueue<string[]>();

    private FakeBybitSocketServer(WebApplication app)
    {
        _app = app;
    }

    public static async Task<FakeBybitSocketServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        // The settings of the app lie next to the tests and would give this server the endpoints of the app; it keeps only its own.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        var server = new FakeBybitSocketServer(app);
        app.UseWebSockets();
        app.Map("/v5/{*path}", server.AcceptAsync);
        await app.StartAsync();
        string http = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        server.Address = http.Replace("http://", "ws://", StringComparison.Ordinal);

        return server;
    }

    /// <summary>
    ///     The topics clients are subscribed to now on <paramref name="path" />, such as <c>/v5/public/linear</c>.
    /// </summary>
    public IReadOnlyCollection<string> Topics(string path)
    {
        return [.. _connections.Values.Where(connection => connection.Path == path).SelectMany(connection => connection.Topics.Keys)];
    }

    /// <summary>
    ///     Waits until no client is subscribed to anything; fails when one still is after five seconds.
    /// </summary>
    public async Task WaitForNoTopicsAsync()
    {
        DateTime until = DateTime.UtcNow + Patience;

        while (_connections.Values.Any(connection => !connection.Topics.IsEmpty))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("Clients are still subscribed.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>
    ///     Pushes the data of a topic to every client subscribed to it on <paramref name="path" />.
    /// </summary>
    public async Task PushAsync(string path, string topic, params JsonObject[] data)
    {
        string message = new JsonObject
        {
            ["topic"] = topic,
            ["type"] = "snapshot",
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["data"] = new JsonArray([.. data])
        }.ToJsonString();

        foreach (Connection connection in _connections.Values.Where(connection => connection.Path == path && connection.Topics.ContainsKey(topic)))
        {
            await connection.SendAsync(message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (WebSocket socket in _connections.Keys)
        {
            socket.Abort();
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    ///     A candle of the topic <c>kline.1</c>; <paramref name="confirm" /> when its minute closed.
    /// </summary>
    public static JsonObject Kline(
        DateTimeOffset openTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume,
        decimal turnover,
        bool confirm = true)
    {
        return new JsonObject
        {
            ["start"] = openTime.ToUnixTimeMilliseconds(),
            ["end"] = openTime.AddMinutes(1).ToUnixTimeMilliseconds() - 1,
            ["interval"] = "1",
            ["open"] = Text(open),
            ["close"] = Text(close),
            ["high"] = Text(high),
            ["low"] = Text(low),
            ["volume"] = Text(volume),
            ["turnover"] = Text(turnover),
            ["confirm"] = confirm,
            ["timestamp"] = openTime.AddMinutes(1).ToUnixTimeMilliseconds()
        };
    }

    /// <summary>
    ///     A trade of the topic <c>publicTrade</c>; <paramref name="side" /> is the side of its taker, <c>Buy</c> or <c>Sell</c>.
    /// </summary>
    public static JsonObject Trade(string symbol, DateTimeOffset time, string side, decimal quantity, decimal price = 100m)
    {
        return new JsonObject
        {
            ["T"] = time.ToUnixTimeMilliseconds(),
            ["s"] = symbol,
            ["S"] = side,
            ["v"] = Text(quantity),
            ["p"] = Text(price),
            ["L"] = "PlusTick",
            ["i"] = Guid.NewGuid().ToString(),
            ["BT"] = false,
            ["seq"] = 1
        };
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private async Task AcceptAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new Connection(socket, context.Request.Path.Value ?? "");
        _connections[socket] = connection;
        var buffer = new byte[64 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, context.RequestAborted);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                await AnswerAsync(connection, JsonNode.Parse(Encoding.UTF8.GetString(buffer, index: 0, received.Count))!.AsObject());
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // The connection broke: the server stops or the client went away.
        }
        finally
        {
            _connections.TryRemove(socket, out _);
        }
    }

    private async Task AnswerAsync(Connection connection, JsonObject request)
    {
        string op = request["op"]?.GetValue<string>() ?? "";
        string? requestId = request["req_id"]?.GetValue<string>();

        switch (op)
        {
            case "subscribe":
                string[] topics = [.. request["args"]!.AsArray().Select(topic => topic!.GetValue<string>())];

                if (topics.FirstOrDefault(RefusedTopics.ContainsKey) is { } refused)
                {
                    await connection.SendAsync(Answer(op, requestId, success: false, $"error:handler not found,topic:{refused}"));

                    break;
                }

                SubscribeRequests.Enqueue(topics);

                foreach (string topic in topics)
                {
                    connection.Topics[topic] = true;
                }

                await connection.SendAsync(Answer(op, requestId, success: true, message: ""));

                break;
            case "unsubscribe":
                foreach (JsonNode? topic in request["args"]!.AsArray())
                {
                    connection.Topics.TryRemove(topic!.GetValue<string>(), out _);
                }

                await connection.SendAsync(Answer(op, requestId, success: true, message: ""));

                break;
            case "ping":
                await connection.SendAsync(Answer(op, requestId, success: true, message: "pong"));

                break;
        }
    }

    private static string Answer(string op, string? requestId, bool success, string message)
    {
        return new JsonObject { ["success"] = success, ["ret_msg"] = message, ["op"] = op, ["conn_id"] = "fake", ["req_id"] = requestId }.ToJsonString();
    }

    private sealed class Connection(WebSocket socket, string path)
    {
        private readonly SemaphoreSlim _sending = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        public string Path { get; } = path;

        public ConcurrentDictionary<string, bool> Topics { get; } = new ConcurrentDictionary<string, bool>();

        public async Task SendAsync(string message)
        {
            await _sending.WaitAsync();

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                }
            }
            finally
            {
                _sending.Release();
            }
        }
    }
}