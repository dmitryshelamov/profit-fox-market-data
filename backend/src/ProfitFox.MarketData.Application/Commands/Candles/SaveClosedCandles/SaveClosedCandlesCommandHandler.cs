using MediatR;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;

/// <summary>
///     Adds the minutes of the candles to the series the loading of candles started and saves those series together. It starts no series
///     itself: the loading starts one with the time before the instrument began to trade covered, and a series started here would have
///     that time asked for page by page. The feed hears of a candle only after it is saved.
/// </summary>
internal sealed class SaveClosedCandlesCommandHandler(ICandleSeriesRepository series, ILiveCandleFeed feed)
    : IRequestHandler<SaveClosedCandlesCommand, SaveClosedCandlesResult>
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public async Task<SaveClosedCandlesResult> Handle(SaveClosedCandlesCommand request, CancellationToken cancellationToken)
    {
        // The last told stands for the minute, but one told without a buy volume never takes the place of one told with it, as in
        // storage: Bybit tells a candle again after a reconnect, when the trades of its minute are no longer all seen.
        var told = request.Candles
            .GroupBy(closed => (closed.InstrumentId, closed.Candle.OpenTime))
            .Select(minute => minute.LastOrDefault(closed => closed.Candle.BuyVolume is not null) ?? minute.Last())
            .GroupBy(closed => closed.InstrumentId)
            .ToDictionary(instrument => instrument.Key, instrument => instrument.ToArray());

        if (told.Count == 0)
        {
            return new SaveClosedCandlesResult(Saved: 0);
        }

        IReadOnlyCollection<CandleSeries> started = await series.GetAsync(told.Keys, cancellationToken);

        if (started.Count == 0)
        {
            return new SaveClosedCandlesResult(Saved: 0);
        }

        foreach (CandleSeries one in started)
        {
            foreach (ClosedCandle closed in told[one.InstrumentId])
            {
                one.Cover(new MinuteRange(closed.Candle.OpenTime, closed.Candle.OpenTime + Minute), [closed.Candle]);
            }
        }

        await series.SaveAsync(started, cancellationToken);

        ClosedCandle[] saved = [.. started.SelectMany(one => told[one.InstrumentId])];
        feed.Publish([.. saved.Select(closed => new LiveCandle(closed.Exchange, closed.InstrumentId, closed.Candle, Closed: true))]);

        return new SaveClosedCandlesResult(saved.Length);
    }
}