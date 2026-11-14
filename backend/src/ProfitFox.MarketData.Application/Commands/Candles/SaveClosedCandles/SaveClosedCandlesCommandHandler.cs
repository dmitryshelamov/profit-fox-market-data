using MediatR;

using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.SaveClosedCandles;

/// <summary>
///     Adds the minutes of the candles to the series the loading of candles started and saves those series together. It starts no series
///     itself: the loading starts one with the time before the instrument began to trade covered, and a series started here would have
///     that time asked for page by page.
/// </summary>
internal sealed class SaveClosedCandlesCommandHandler(ICandleSeriesRepository series) : IRequestHandler<SaveClosedCandlesCommand, SaveClosedCandlesResult>
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public async Task<SaveClosedCandlesResult> Handle(SaveClosedCandlesCommand request, CancellationToken cancellationToken)
    {
        // A candle told again knows more of the trades of its minute, so the last told stands for the minute.
        var told = request.Candles
            .GroupBy(closed => (closed.InstrumentId, closed.Candle.OpenTime))
            .Select(minute => minute.Last())
            .GroupBy(closed => closed.InstrumentId)
            .ToDictionary(instrument => instrument.Key, instrument => instrument.Select(closed => closed.Candle).ToArray());

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
            foreach (Candle candle in told[one.InstrumentId])
            {
                one.Cover(new MinuteRange(candle.OpenTime, candle.OpenTime + Minute), [candle]);
            }
        }

        await series.SaveAsync(started, cancellationToken);

        return new SaveClosedCandlesResult(started.Sum(one => told[one.InstrumentId].Length));
    }
}