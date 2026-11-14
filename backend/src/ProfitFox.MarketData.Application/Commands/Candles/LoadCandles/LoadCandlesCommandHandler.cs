using MediatR;

using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments.Base;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;

/// <summary>
///     Loads the newest uncovered page of the series of an instrument from the exchange of its listing. A series loaded for the first time covers
///     the time before the instrument started to trade with no candles, so that time is never asked for.
/// </summary>
internal sealed class LoadCandlesCommandHandler(
    IInstrumentRepository instruments,
    ICandleSeriesRepository series,
    IBybitCandles bybit,
    ITInvestCandles tinvest) : IRequestHandler<LoadCandlesCommand, bool>
{
    // The exchange may still give the candle of a minute that closed a moment ago unfinished; covered, it would stay so for good.
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(5);

    // The longest range one request gives: 1000 candles on Bybit, one day of minute candles on T-Invest.
    private static readonly TimeSpan BybitPage = TimeSpan.FromMinutes(1000);

    private static readonly TimeSpan TInvestPage = TimeSpan.FromDays(1);

    public async Task<bool> Handle(LoadCandlesCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = await instruments.GetAsync(request.InstrumentId, cancellationToken)
            ?? throw new InvalidOperationException($"The catalog has no instrument {request.InstrumentId}.");

        MinuteRange? range = Closed(request.Range, request.RequestedAt);

        if (range is null)
        {
            return false;
        }

        CandleSeries? stored = await series.GetAsync(instrument.Id, cancellationToken);
        CandleSeries current = stored ?? await StartAsync(instrument, range, cancellationToken);
        IReadOnlyList<MinuteRange> uncovered = current.Uncovered(range);

        if (uncovered.Count == 0)
        {
            // A new series may cover all of the range with the time before trading started; that is saved still.
            if (stored is null)
            {
                await series.SaveAsync(current, cancellationToken);
            }

            return false;
        }

        MinuteRange newestUncovered = uncovered[^1];
        DateTimeOffset pageFrom = newestUncovered.Until - PageOf(instrument.Listing);
        var page = new MinuteRange(pageFrom > newestUncovered.From ? pageFrom : newestUncovered.From, newestUncovered.Until);

        current.Cover(page, await GetAsync(instrument.Listing, page, cancellationToken));
        await series.SaveAsync(current, cancellationToken);

        return current.Uncovered(range).Count > 0;
    }

    // The part of the range whose minutes closed by the request; null when none did.
    private static MinuteRange? Closed(MinuteRange range, DateTimeOffset requestedAt)
    {
        DateTimeOffset closedUntil = StartOfMinute(requestedAt - Settling);

        if (closedUntil <= range.From)
        {
            return null;
        }

        return closedUntil < range.Until ? new MinuteRange(range.From, closedUntil) : range;
    }

    private async Task<CandleSeries> StartAsync(Instrument instrument, MinuteRange range, CancellationToken cancellationToken)
    {
        var created = CandleSeries.Create(instrument.Id);

        DateTimeOffset? start = instrument.Listing switch
        {
            BybitListedInstrument listing => await bybit.GetTradingStartAsync(listing, range.From, cancellationToken),
            TInvestListedShare share => await tinvest.GetTradingStartAsync(share, range.From, cancellationToken),
            _ => throw Unsupported(instrument.Listing)
        };

        if (start is null)
        {
            return created;
        }

        // The start of its minute, so a trade of that minute is not covered away.
        DateTimeOffset startMinute = StartOfMinute(start.Value);
        DateTimeOffset until = startMinute < range.Until ? startMinute : range.Until;

        if (until > range.From)
        {
            created.Cover(new MinuteRange(range.From, until), []);
        }

        return created;
    }

    private Task<IReadOnlyList<Candle>> GetAsync(ListedInstrument listing, MinuteRange page, CancellationToken cancellationToken)
    {
        return listing switch
        {
            BybitListedInstrument bybitListing => bybit.GetAsync(bybitListing, page, cancellationToken),
            TInvestListedShare share => tinvest.GetAsync(share, page, cancellationToken),
            _ => throw Unsupported(listing)
        };
    }

    private static TimeSpan PageOf(ListedInstrument listing)
    {
        return listing switch
        {
            BybitListedInstrument => BybitPage,
            TInvestListedShare => TInvestPage,
            _ => throw Unsupported(listing)
        };
    }

    private static DateTimeOffset StartOfMinute(DateTimeOffset time)
    {
        return new DateTimeOffset(time.UtcTicks - time.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }

    private static NotSupportedException Unsupported(ListedInstrument listing)
    {
        return new NotSupportedException($"Candles of a listing of {listing.GetType().Name} are not loaded.");
    }
}