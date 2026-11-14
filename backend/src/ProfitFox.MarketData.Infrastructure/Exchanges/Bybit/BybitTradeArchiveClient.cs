using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Infrastructure.Exchanges.Retry;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the daily archives of trades from <c>public.bybit.com</c>: a gzipped CSV a day of an instrument. A file is read as it comes
///     and summed by minute; nothing of it is kept on disk or in memory but the sums. The trades of a minute in the archive add up to
///     the volume of its candle exactly, so a candle gets its buy volume only when they do.
/// </summary>
internal sealed class BybitTradeArchiveClient(HttpClient http, ExchangeRetryExecutor retry) : IBybitTradeArchiveClient
{
    // A time of the archive below this is in seconds (linear), above it in milliseconds (spot).
    private const decimal FirstMillisecondTime = 100_000_000_000m;

    // The archive answers 403 to a request that names no client, which HttpClient does not do by itself.
    private static readonly ProductInfoHeaderValue Client = new ProductInfoHeaderValue("market-data", "1.0");

    public async Task<IReadOnlyList<Candle>?> FillBuyVolumesAsync(
        BybitListedInstrument listing,
        DateOnly day,
        IReadOnlyCollection<Candle> candles,
        CancellationToken cancellationToken)
    {
        Dictionary<DateTimeOffset, Sums>? minutes = await retry.ExecuteAsync(token => SumAsync(listing, day, token), cancellationToken);

        if (minutes is null)
        {
            return null;
        }

        return
        [
            .. candles.Select(candle => minutes.TryGetValue(candle.OpenTime, out Sums? sums) && sums.Traded == candle.Volume
                ? new Candle(candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.Turnover, sums.Bought)
                : candle)
        ];
    }

    // The trades of the file summed by the minute they were made in; null when Bybit has no such file. A file that cannot be had or
    // breaks on the way is a transient failure: it is read again from the start. A file laid out in a way not known is not: reading it
    // again would not help.
    private async Task<Dictionary<DateTimeOffset, Sums>?> SumAsync(BybitListedInstrument listing, DateOnly day, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Address(listing, day));
            request.Headers.UserAgent.Add(Client);

            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new TransientExchangeException($"Bybit answered {(int)response.StatusCode} for the archive of trades of {listing.Symbol} of {day:O}.");
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var unpacked = new GZipStream(body, CompressionMode.Decompress);
            using var reader = new StreamReader(unpacked);

            var columns = Columns.Of(await reader.ReadLineAsync(cancellationToken) ?? "", listing, day);
            var minutes = new Dictionary<DateTimeOffset, Sums>();

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length > 0)
                {
                    Count(line, columns, minutes);
                }
            }

            return minutes;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
        {
            throw new TransientExchangeException($"The archive of trades of {listing.Symbol} of {day:O} cannot be read from Bybit.", exception);
        }
    }

    // Linear and spot lay their files out apart: trading/BTCUSDT/BTCUSDT2026-11-13.csv.gz and spot/BTCUSDT/BTCUSDT_2026-11-13.csv.gz.
    private static string Address(BybitListedInstrument listing, DateOnly day)
    {
        return listing.Category switch
        {
            BybitCategory.Linear => $"trading/{listing.Symbol}/{listing.Symbol}{day:yyyy-MM-dd}.csv.gz",
            BybitCategory.Spot => $"spot/{listing.Symbol}/{listing.Symbol}_{day:yyyy-MM-dd}.csv.gz",
            _ => throw new NotSupportedException($"Bybit has no archive of trades of {listing.Category} instruments.")
        };
    }

    private static void Count(string line, Columns columns, Dictionary<DateTimeOffset, Sums> minutes)
    {
        decimal time = decimal.Parse(Field(line, columns.Time), NumberStyles.Float, CultureInfo.InvariantCulture);
        long milliseconds = time < FirstMillisecondTime ? (long)(time * 1000m) : (long)time;
        DateTimeOffset minute = MinuteRange.StartOfMinute(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
        decimal quantity = decimal.Parse(Field(line, columns.Quantity), NumberStyles.Float, CultureInfo.InvariantCulture);

        if (!minutes.TryGetValue(minute, out Sums? sums))
        {
            minutes[minute] = sums = new Sums();
        }

        sums.Traded += quantity;

        // The side is that of the taker: a buy is a trade the buyer started.
        if (Field(line, columns.Side).Equals("buy", StringComparison.OrdinalIgnoreCase))
        {
            sums.Bought += quantity;
        }
    }

    private static ReadOnlySpan<char> Field(ReadOnlySpan<char> line, int index)
    {
        for (var skipped = 0; skipped < index; skipped++)
        {
            int comma = line.IndexOf(',');

            if (comma < 0)
            {
                throw new FormatException("A line of an archive of trades of Bybit is shorter than its header.");
            }

            line = line[(comma + 1)..];
        }

        int end = line.IndexOf(',');

        return end < 0 ? line : line[..end];
    }

    // Where the time, the side and the quantity of a trade are in a line: linear names the quantity size, spot names it volume.
    private sealed record Columns(int Time, int Side, int Quantity)
    {
        public static Columns Of(string header, BybitListedInstrument listing, DateOnly day)
        {
            string[] names = header.Split(',');
            int time = Array.IndexOf(names, "timestamp");
            int side = Array.IndexOf(names, "side");
            int quantity = Math.Max(Array.IndexOf(names, "size"), Array.IndexOf(names, "volume"));

            if (time < 0 || side < 0 || quantity < 0)
            {
                throw new FormatException($"The archive of trades of {listing.Symbol} of {day:O} has an unknown header: {header}");
            }

            return new Columns(time, side, quantity);
        }
    }

    private sealed class Sums
    {
        public decimal Traded { get; set; }

        public decimal Bought { get; set; }
    }
}