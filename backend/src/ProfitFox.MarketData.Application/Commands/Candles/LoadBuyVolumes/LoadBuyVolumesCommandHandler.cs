using MediatR;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadBuyVolumes;

/// <summary>
///     Goes through the whole days of the range from the newest back. A day is settled once all of it is covered: its candles without
///     volume have nothing bought, the others take their buy volume from the archive of trades of the day where it adds up. The day and
///     its candles are saved together, so a day is never settled without the buy volumes found for it.
/// </summary>
internal sealed class LoadBuyVolumesCommandHandler(
    IInstrumentRepository instruments,
    ICandleSeriesRepository series,
    IBybitTradeArchiveClient archive) : IRequestHandler<LoadBuyVolumesCommand, LoadBuyVolumesResult>
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    // Bybit publishes the archive of a day one to two hours after the day ends.
    private static readonly TimeSpan Published = TimeSpan.FromHours(2);

    // A day whose archive is still not there this long after it ended has none.
    private static readonly TimeSpan NoArchive = TimeSpan.FromHours(48);

    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<LoadBuyVolumesResult> Handle(LoadBuyVolumesCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = await instruments.GetAsync(request.InstrumentId, cancellationToken)
            ?? throw new InstrumentNotFoundException(request.InstrumentId);

        // Only Bybit has an archive of trades; the candles of T-Invest tell their buy volume themselves.
        if (instrument.Listing.Exchange != Exchange.Bybit)
        {
            return LoadBuyVolumesResult.NothingLeft;
        }

        CandleSeries? stored = await series.GetAsync(instrument.Id, cancellationToken);

        if (stored is null)
        {
            return LoadBuyVolumesResult.NothingLeft;
        }

        var listing = (BybitListedInstrument)instrument.Listing;
        var settledAny = false;

        foreach (MinuteRange day in Days(request.Range, request.RequestedAt))
        {
            // A day with minutes still to load waits for them: candles loaded into a settled day would never get a buy volume.
            if (stored.Unsettled(day).Count == 0 || stored.Uncovered(day).Count > 0)
            {
                continue;
            }

            IReadOnlyList<Candle> without = await series.GetCandlesWithoutBuyVolumeAsync(instrument.Id, day, cancellationToken);
            Candle[] traded = [.. without.Where(candle => candle.Volume > 0m)];
            IReadOnlyList<Candle>? fromArchive = traded.Length == 0
                ? []
                : await archive.FillBuyVolumesAsync(listing, DateOnly.FromDateTime(day.From.UtcDateTime), traded, cancellationToken);

            if (fromArchive is null)
            {
                // The archive may still be published; an older day goes on meanwhile.
                if (request.RequestedAt < day.Until + NoArchive)
                {
                    continue;
                }

                fromArchive = [];
            }

            stored.Cover(day, [.. without.Where(candle => candle.Volume == 0m).Select(NothingBought), .. fromArchive.Where(candle => candle.BuyVolume is not null)]);
            stored.Settle(day);
            settledAny = true;

            // One archive a command: the others of the series wait for the next one.
            if (traded.Length > 0)
            {
                await series.SaveAsync(stored, cancellationToken);

                return LoadBuyVolumesResult.MoreLeft;
            }
        }

        if (settledAny)
        {
            await series.SaveAsync(stored, cancellationToken);
        }

        return LoadBuyVolumesResult.NothingLeft;
    }

    // The whole UTC days of the range whose archive should be published by now, the newest first.
    private static IEnumerable<MinuteRange> Days(MinuteRange range, DateTimeOffset requestedAt)
    {
        DateTimeOffset publishedUntil = StartOfDay(requestedAt - Published);
        DateTimeOffset until = StartOfDay(range.Until < publishedUntil ? range.Until : publishedUntil);

        for (DateTimeOffset from = until - Day; from >= range.From; from -= Day)
        {
            yield return new MinuteRange(from, from + Day);
        }
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero);
    }

    // No trades, so nothing bought.
    private static Candle NothingBought(Candle candle)
    {
        return new Candle(candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.Turnover, buyVolume: 0m);
    }
}