using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     Reads the minute candles of spot and linear from the public API of Bybit, one request a call. Bybit gives the volume in the base
///     asset and the turnover in the quote asset.
/// </summary>
internal sealed class BybitCandlesClient(IBybitRestClient bybit, BybitRequestGate gate, TimeProvider time) : IExchangeCandlesClient
{
    // The largest page of candles Bybit gives.
    private const int PageSize = 1000;

    public TimeSpan PageLength => TimeSpan.FromMinutes(PageSize);

    public bool Serves(ListedInstrument listing)
    {
        return listing is BybitListedInstrument;
    }

    public async Task<IReadOnlyList<Candle>> GetAsync(ListedInstrument listed, MinuteRange range, CancellationToken cancellationToken)
    {
        var listing = (BybitListedInstrument)listed;

        // Bybit takes both ends as included: the last millisecond before the end keeps the minute that starts there out.
        BybitResponse<BybitKline> response = await gate.SendAsync(
            async token => BybitResponses.DataOf(
                await bybit.V5Api.ExchangeData.GetKlinesAsync(
                    ToBybit(listing.Category),
                    listing.Symbol,
                    KlineInterval.OneMinute,
                    range.From.UtcDateTime,
                    range.Until.UtcDateTime.AddMilliseconds(-1),
                    PageSize,
                    token),
                NoCandles(listing)),
            cancellationToken);

        // Bybit gives the newest first; a candle outside the range, such as the one of a minute still open, is left out.
        return
        [
            .. response.List
                .Select(ToDomain)
                .Where(candle => candle.OpenTime >= range.From && candle.OpenTime < range.Until)
                .OrderBy(candle => candle.OpenTime)
        ];
    }

    // A linear contract tells its launch time; a spot pair tells none, so its first daily candle since then gives the day it started.
    public async Task<DateTimeOffset?> GetTradingStartAsync(ListedInstrument listed, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var listing = (BybitListedInstrument)listed;

        if (listing.LaunchTime is { } launchTime)
        {
            return launchTime;
        }

        BybitResponse<BybitKline> response = await gate.SendAsync(
            async token => BybitResponses.DataOf(
                await bybit.V5Api.ExchangeData.GetKlinesAsync(
                    ToBybit(listing.Category),
                    listing.Symbol,
                    KlineInterval.OneDay,
                    since.UtcDateTime,
                    time.GetUtcNow().UtcDateTime,
                    PageSize,
                    token),
                NoCandles(listing)),
            cancellationToken);

        // A full page holds only the newest days: the pair may have traded before the oldest of them.
        BybitKline[] days = [.. response.List];

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

    private static string NoCandles(BybitListedInstrument listing)
    {
        return $"Bybit gave no candles of {listing.Category} {listing.Symbol}";
    }
}