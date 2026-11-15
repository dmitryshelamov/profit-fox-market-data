using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;

using Bybit.Net;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Counts every HTTP attempt to the REST API of Bybit, retries included, as <c>profitfox.bybit.rest.requests</c> of the meter
///     <c>ProfitFox.Bybit</c> by endpoint, HTTP code and outcome (<c>ok</c>, <c>rate_limited</c>, <c>error</c>), with the names and tags of
///     profit-fox, so a dashboard sums the load of both on the IP they share. It sits on every client of the factory and lets a request to
///     any other address pass uncounted, such as one to the archive of trades.
/// </summary>
internal sealed class BybitRestMetricsHandler(IMeterFactory meters) : DelegatingHandler
{
    public const string MeterName = "ProfitFox.Bybit";

    private static readonly Uri Bybit = new Uri(BybitEnvironment.Live.RestBaseAddress);

    private readonly Counter<long> _requests = meters.Create(MeterName).CreateCounter<long>("profitfox.bybit.rest.requests", "{request}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Uri.Compare(request.RequestUri, Bybit, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var endpoint = new KeyValuePair<string, object?>("bybit.endpoint", request.RequestUri!.AbsolutePath);
        HttpResponseMessage response;

        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch
        {
            // A timeout of the client cancels the same token as a caller who gives up, so neither is told apart from a broken connection.
            _requests.Add(1, endpoint, new KeyValuePair<string, object?>("bybit.outcome", "error"));

            throw;
        }

        _requests.Add(
            1,
            endpoint,
            new KeyValuePair<string, object?>("http.response.status_code", (int)response.StatusCode),
            new KeyValuePair<string, object?>("bybit.outcome", await OutcomeAsync(response, cancellationToken)));

        return response;
    }

    // 403 is how Bybit bans an IP, and how one edge of its CloudFront refuses a country; the gate pauses on both.
    private static async Task<string> OutcomeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return "rate_limited";
        }

        if (!response.IsSuccessStatusCode)
        {
            return "error";
        }

        // The SDK reads the body after the handler: it is buffered, so the SDK still reads it whole.
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return RetCode(body) switch
        {
            null or 0 => "ok",
            10006 => "rate_limited",
            _ => "error"
        };
    }

    // The top-level retCode of an answer; none when the body is not a JSON object or has no such field as a whole number.
    private static int? RetCode(byte[] body)
    {
        try
        {
            var reader = new Utf8JsonReader(body);
            reader.Read();

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            // Bybit writes retCode first, so the rest of a page of candles is never read.
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                bool found = reader.ValueTextEquals("retCode"u8);
                reader.Read();

                if (found)
                {
                    return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int code) ? code : null;
                }

                reader.Skip();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}