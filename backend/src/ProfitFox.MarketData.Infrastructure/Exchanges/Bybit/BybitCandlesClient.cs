using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the minute candles of spot and linear from the public API of Bybit, one request a call. Bybit gives the volume in the base
///     asset and the turnover in the quote asset.
/// </summary>
internal sealed class BybitCandlesClient(IBybitRestClient bybit, TimeProvider time) : IBybitCandlesClient
{
    // The largest page of candles Bybit gives.
    private const int PageSize = 1000;

    public async Task<IReadOnlyList<Candle>> GetAsync(BybitListedInstrument listing, MinuteRange range, CancellationToken cancellationToken)
    {
        // Bybit takes both ends as included: the last millisecond before the end keeps the minute that starts there out.
        HttpResult<BybitResponse<BybitKline>> response = await bybit.V5Api.ExchangeData.GetKlinesAsync(
            ToBybit(listing.Category),
            listing.Symbol,
            KlineInterval.OneMinute,
            range.From.UtcDateTime,
            range.Until.UtcDateTime.AddMilliseconds(-1),
            PageSize,
            cancellationToken);
        ThrowIfFailed(response, listing);

        // Bybit gives the newest first; a candle outside the range, such as the one of a minute still open, is left out.
        return
        [
            .. response.Data!.List
                .Select(ToDomain)
                .Where(candle => candle.OpenTime >= range.From && candle.OpenTime < range.Until)
                .OrderBy(candle => candle.OpenTime)
        ];
    }

    // A linear contract tells its launch time; a spot pair tells none, so its first daily candle since then gives the day it started.
    public async Task<DateTimeOffset?> GetTradingStartAsync(BybitListedInstrument listing, DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (listing.LaunchTime is { } launchTime)
        {
            return launchTime;
        }

        HttpResult<BybitResponse<BybitKline>> response = await bybit.V5Api.ExchangeData.GetKlinesAsync(
            ToBybit(listing.Category),
            listing.Symbol,
            KlineInterval.OneDay,
            since.UtcDateTime,
            time.GetUtcNow().UtcDateTime,
            PageSize,
            cancellationToken);
        ThrowIfFailed(response, listing);

        // A full page holds only the newest days: the pair may have traded before the oldest of them.
        BybitKline[] days = [.. response.Data!.List];

        if (days.Length == 0 || days.Length == PageSize)
        {
            return null;
        }

        return days.Min(day => Utc(day.StartTime));
    }

    private static Candle ToDomain(BybitKline kline)
    {
        return new Candle(
            Utc(kline.StartTime),
            kline.OpenPrice,
            kline.HighPrice,
            kline.LowPrice,
            kline.ClosePrice,
            kline.Volume,
            kline.QuoteVolume);
    }

    private static DateTimeOffset Utc(DateTime time)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }

    private static Category ToBybit(BybitCategory category)
    {
        return category switch
        {
            BybitCategory.Spot => Category.Spot,
            BybitCategory.Linear => Category.Linear,
            _ => throw new NotSupportedException($"Candles of {category} instruments of Bybit are not loaded.")
        };
    }

    private static void ThrowIfFailed<T>(HttpResult<T> response, BybitListedInstrument listing)
    {
        if (!response.Success)
        {
            throw new HttpRequestException(
                $"Bybit gave no candles of {listing.Category} {listing.Symbol}: {response.Error}",
                inner: null,
                response.ResponseStatusCode);
        }
    }
}