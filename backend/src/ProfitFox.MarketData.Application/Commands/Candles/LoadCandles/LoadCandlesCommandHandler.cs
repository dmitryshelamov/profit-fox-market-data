using MediatR;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Entities.Candles;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Candles;

namespace ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;

/// <summary>
///     Loads the newest uncovered page of the series of an instrument through the client of its exchange. A series loaded for the first
///     time covers the time before the instrument started to trade with no candles, so that time is never asked for.
/// </summary>
internal sealed class LoadCandlesCommandHandler(
    IInstrumentRepository instruments,
    ICandleSeriesRepository series,
    IEnumerable<IExchangeCandlesClient> exchanges) : IRequestHandler<LoadCandlesCommand, LoadCandlesResult>
{
    // The exchange may still give the candle of a minute that closed a moment ago unfinished; covered, it would stay so for good.
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(5);

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

        MinuteRange page = NewestPage(uncovered[^1], exchange.PageLength);

        current.Cover(page, await exchange.GetAsync(instrument.Listing, page, cancellationToken));
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

    // The last page of an uncovered part: the whole part when it is no longer than a page.
    private static MinuteRange NewestPage(MinuteRange uncovered, TimeSpan pageLength)
    {
        DateTimeOffset pageFrom = uncovered.Until - pageLength;

        return new MinuteRange(pageFrom > uncovered.From ? pageFrom : uncovered.From, uncovered.Until);
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