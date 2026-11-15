using System.Collections.ObjectModel;

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
///     Goes through the whole days of the range from the newest back. A day is settled once all of it is covered: its candles take
///     their buy volume from the trades of their minutes in the archive of the day, where those add up to the volume of the candle. A
///     day none of whose candles traded needs no archive. The day and its candles are saved together, so a day is never settled without
///     the buy volumes found for it.
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

    private static readonly IReadOnlyDictionary<DateTimeOffset, TradedVolume> NoTrades = ReadOnlyDictionary<DateTimeOffset, TradedVolume>.Empty;

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
        LoadBuyVolumesResult result = LoadBuyVolumesResult.NothingLeft;
        var settledAny = false;

        foreach (MinuteRange day in Days(request.Range, request.RequestedAt))
        {
            // A day with minutes still to load waits for them: candles loaded into a settled day would never get a buy volume.
            if (stored.Unsettled(day).Count == 0 || stored.Uncovered(day).Count > 0)
            {
                continue;
            }

            IReadOnlyList<Candle> without = await series.GetCandlesWithoutBuyVolumeAsync(instrument.Id, day, cancellationToken);
            bool needsArchive = without.Any(candle => candle.Volume > 0m);
            IReadOnlyDictionary<DateTimeOffset, TradedVolume>? trades = needsArchive
                ? await archive.GetTradesAsync(listing, DateOnly.FromDateTime(day.From.UtcDateTime), cancellationToken)
                : NoTrades;

            // The archive may still be published; an older day goes on meanwhile.
            if (trades is null && request.RequestedAt < day.Until + NoArchive)
            {
                continue;
            }

            stored.Settle(day, WithBuyVolumes(without, trades ?? NoTrades));
            settledAny = true;

            // One archive a command: the other days of the series wait for the next one.
            if (needsArchive)
            {
                result = LoadBuyVolumesResult.MoreLeft;

                break;
            }
        }

        if (settledAny)
        {
            await series.SaveAsync(stored, cancellationToken);
        }

        return result;
    }

    // The candles that get a buy volume from the trades of their minutes. A minute without trades adds up only to a candle without
    // volume, which so has nothing bought.
    private static Candle[] WithBuyVolumes(IReadOnlyList<Candle> candles, IReadOnlyDictionary<DateTimeOffset, TradedVolume> trades)
    {
        return
        [
            .. candles
                .Select(candle => candle.WithBuyVolume(trades.GetValueOrDefault(candle.OpenTime, TradedVolume.None)))
                .Where(candle => candle.BuyVolume is not null)
        ];
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
}