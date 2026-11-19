using MediatR;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;

/// <summary>
///     Loads the newest uncovered page of the series of an instrument through the client of its exchange; for a caller whose fresh minutes
///     may wait, the newest page of the older ones while those are left and the fresh ones are under an hour. A series loaded for the first
///     time covers the time before the instrument started to trade with no candles, so that time is never asked for. A whole history
///     ends at the first page of its oldest part the exchange gives no candle for (<see cref="LoadCandlesCommand.WholeHistory" />). Any
///     other history whose oldest part gives an empty page asks when the instrument started to trade, and ends there when the page lies
///     before that start: a history made longer after the first load is not walked back page by page through the time before trading.
/// </summary>
internal sealed class LoadCandlesCommandHandler(
    IInstrumentRepository instruments,
    ICandleSeriesRepository series,
    IEnumerable<IExchangeCandlesClient> exchanges) : IRequestHandler<LoadCandlesCommand, LoadCandlesResult>
{
    // The exchange may still give the candle of a minute that closed a moment ago unfinished; covered, it would stay so for good.
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(5);

    // How long the uncovered minutes at the end of a range may get while older ones load (LoadCandlesCommand.FreshMayWait). The stream
    // covers only the minutes that had a trade, so a share that trades now and then has a few fresh minutes uncovered at every turn of
    // the background loading, and its one request of the round would go to them, leaving the history where it is.
    private static readonly TimeSpan FreshWhileHistoryLoads = TimeSpan.FromHours(1);

    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<LoadCandlesResult> Handle(LoadCandlesCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = await instruments.GetAsync(request.InstrumentId, cancellationToken)
            ?? throw new InstrumentNotFoundException(request.InstrumentId);

        MinuteRange? range = Closed(request.Range, request.RequestedAt);

        if (range is null)
        {
            return LoadCandlesResult.NothingLeft;
        }

        IExchangeCandlesClient exchange = exchanges.FirstOrDefault(client => client.Exchange == instrument.Listing.Exchange)
            ?? throw new NotSupportedException($"Candles of {instrument.Listing.Exchange} are not loaded.");

        CandleSeries? stored = await series.GetAsync(instrument.Id, cancellationToken);
        CandleSeries current = stored ?? await StartAsync(exchange, instrument, range, cancellationToken);
        IReadOnlyList<MinuteRange> uncovered = current.Uncovered(range);

        if (uncovered.Count == 0)
        {
            // A new series may cover all of the range with the time before trading started; that is saved still.
            if (stored is null)
            {
                await series.SaveAsync(current, cancellationToken);
            }

            return LoadCandlesResult.NothingLeft;
        }

        IReadOnlyList<MinuteRange> loading = request.FreshMayWait ? WithoutWaitingFresh(uncovered, range) : uncovered;
        MinuteRange page = NewestPage(loading[^1], exchange.PageLength);
        IReadOnlyList<Candle> candles = await exchange.GetAsync(instrument.Listing, page, cancellationToken);

        // Only once everything newer is covered, but for the fresh minutes that wait: an empty page between covered parts is a pause of
        // trading, not the time before it.
        bool emptyOldest = candles.Count == 0 && loading is [{ } oldest] && oldest.From == range.From;

        if (emptyOldest && (request.WholeHistory || await IsBeforeTradingAsync(exchange, instrument, range, page, cancellationToken)))
        {
            CoverBeforeTrading(current, new MinuteRange(range.From, page.Until));
        }
        else
        {
            current.Cover(page, candles);
        }

        await series.SaveAsync(current, cancellationToken);

        return current.Uncovered(range).Count > 0 ? LoadCandlesResult.MoreLeft : LoadCandlesResult.NothingLeft;
    }

    // The part of the range whose minutes closed by the request; null when none did.
    private static MinuteRange? Closed(MinuteRange range, DateTimeOffset requestedAt)
    {
        DateTimeOffset closedUntil = MinuteRange.StartOfMinute(requestedAt - Settling);

        if (closedUntil <= range.From)
        {
            return null;
        }

        return closedUntil < range.Until ? new MinuteRange(range.From, closedUntil) : range;
    }

    // The uncovered parts without the fresh one at the end of the range while it is under FreshWhileHistoryLoads and older ones are left.
    private static IReadOnlyList<MinuteRange> WithoutWaitingFresh(IReadOnlyList<MinuteRange> uncovered, MinuteRange range)
    {
        MinuteRange newest = uncovered[^1];
        bool waits = uncovered.Count > 1 && newest.Until == range.Until && newest.Until - newest.From < FreshWhileHistoryLoads;

        return waits ? uncovered.Take(uncovered.Count - 1).ToList() : uncovered;
    }

    // The last page of an uncovered part: the whole part when it is no longer than a page.
    private static MinuteRange NewestPage(MinuteRange uncovered, TimeSpan pageLength)
    {
        DateTimeOffset pageFrom = uncovered.Until - pageLength;

        return new MinuteRange(pageFrom > uncovered.From ? pageFrom : uncovered.From, uncovered.Until);
    }

    // Whether the page ends before the instrument started to trade, so all of the range back from it is the time before trading. An
    // empty page after the start is a pause of trading and covers only itself.
    private static async Task<bool> IsBeforeTradingAsync(
        IExchangeCandlesClient exchange,
        Instrument instrument,
        MinuteRange range,
        MinuteRange page,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? start = await exchange.GetTradingStartAsync(instrument.Listing, range.From, cancellationToken);

        return start is { } known && MinuteRange.StartOfMinute(known) >= page.Until;
    }

    // The time before the instrument traded: covered with no candles, and settled at once, as it has no trades to look a buy volume up
    // in. Settled here, the days of years before trading need no pass of the buy volumes each.
    private static void CoverBeforeTrading(CandleSeries current, MinuteRange beforeTrading)
    {
        current.Cover(beforeTrading, []);
        current.Settle(beforeTrading, []);
    }

    // A new series with the part of the range before the instrument started to trade covered, when the exchange tells the start.
    private static async Task<CandleSeries> StartAsync(
        IExchangeCandlesClient exchange,
        Instrument instrument,
        MinuteRange range,
        CancellationToken cancellationToken)
    {
        var created = CandleSeries.Create(instrument.Id);
        DateTimeOffset? start = await exchange.GetTradingStartAsync(instrument.Listing, range.From, cancellationToken);

        if (start is null)
        {
            return created;
        }

        // The start of its minute, so a trade of that minute is not covered away.
        DateTimeOffset startMinute = MinuteRange.StartOfMinute(start.Value);
        DateTimeOffset until = startMinute < range.Until ? startMinute : range.Until;

        if (until > range.From)
        {
            created.Cover(new MinuteRange(range.From, until), []);
        }

        return created;
    }
}