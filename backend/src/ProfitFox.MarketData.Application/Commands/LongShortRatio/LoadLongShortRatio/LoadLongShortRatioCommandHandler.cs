using MediatR;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Contracts.LongShortRatio.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Ports.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.LongShortRatio;

namespace ProfitFox.MarketData.Application.Commands.LongShortRatio.LoadLongShortRatio;

/// <summary>
///     Loads the next uncovered page of the long/short ratio of a linear contract of Bybit quoted in USDT; Bybit keeps none of a
///     contract quoted in USDC, and a spot pair has none. The range is cut to whole steps of the series, to the time since the contract
///     was launched, and to the points Bybit has surely told by now. The feed hears of the points of a page after the coverage once
///     the page is saved; a page of history, the first page of a new series among them, is not told: a year of the market is about a
///     hundred million points.
/// </summary>
internal sealed class LoadLongShortRatioCommandHandler(
    IInstrumentRepository instruments,
    ILongShortRatioSeriesRepository series,
    IBybitLongShortRatioClient bybit,
    ILiveFeed<InstrumentLongShortRatioInternal> feed) : IRequestHandler<LoadLongShortRatioCommand, LoadLongShortRatioResult>
{
    // The asset the contracts Bybit keeps the ratio of are quoted in.
    private const string QuoteAsset = "USDT";

    // Bybit tells the point of a step a minute or two after the step starts; covered before that, the step would stay without it. The
    // same wait, for the same reason, as the Publishing of LoadOpenInterestCommandHandler.
    private static readonly TimeSpan Publishing = TimeSpan.FromMinutes(3);

    // How old the newest points of a contract may get while its history loads. Its rounds come back to it in minutes then, and each
    // would spend its request on the one step told since the round before, leaving the history where it is.
    private static readonly TimeSpan FreshWhileHistoryLoads = TimeSpan.FromHours(1);

    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    /// <exception cref="NotSupportedException">The instrument is not of Bybit.</exception>
    public async Task<LoadLongShortRatioResult> Handle(LoadLongShortRatioCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = await instruments.GetAsync(request.InstrumentId, cancellationToken)
            ?? throw new InstrumentNotFoundException(request.InstrumentId);

        if (instrument.Listing.Exchange != Exchange.Bybit)
        {
            throw new NotSupportedException($"The long/short ratio of {instrument.Listing.Exchange} is not loaded.");
        }

        var listing = (BybitListedInstrument)instrument.Listing;

        // The step that starts last among those told by now is inside the range, the one after it is not.
        DateTimeOffset until = LongShortRatioSeries.StartOfStep(request.RequestedAt - Publishing) + LongShortRatioSeries.Step;
        DateTimeOffset from = FirstStep(request.From, listing);

        if (listing.Category != BybitCategory.Linear || listing.QuoteAsset != QuoteAsset || until <= from)
        {
            return LoadLongShortRatioResult.NothingLeft;
        }

        LongShortRatioSeries? stored = await series.GetAsync(instrument.Id, cancellationToken);

        if (Page(stored, from, until) is not { } page)
        {
            return LoadLongShortRatioResult.NothingLeft;
        }

        bool afterCoverage = stored is not null && page.From >= stored.CoveredUntil;
        IReadOnlyList<BybitLongShortRatioPoint> told = await bybit.GetAsync(listing, page.From, page.Until, cancellationToken);
        LongShortRatioPoint[] points = [.. told.Select(point => new LongShortRatioPoint(instrument.Id, point.Time, point.Long, point.Short))];
        LongShortRatioSeries current = Covered(stored, instrument.Id, page.From, page.Until, points);

        await series.SaveAsync(current, cancellationToken);

        if (afterCoverage && points.Length > 0)
        {
            feed.Publish([.. points.Select(point => point.ToInternal())]);
        }

        return Page(current, from, until) is null ? LoadLongShortRatioResult.NothingLeft : LoadLongShortRatioResult.MoreLeft;
    }

    // The next page to load: the newest one of the range for a new series; else the oldest one after the coverage, so the coverage
    // stays one stretch; else the newest one before it. While history is left, the time after the coverage waits until it is
    // FreshWhileHistoryLoads long. None when the series covers the range.
    private (DateTimeOffset From, DateTimeOffset Until)? Page(LongShortRatioSeries? stored, DateTimeOffset from, DateTimeOffset until)
    {
        if (stored is null)
        {
            return (Later(until - bybit.PageLength, from), until);
        }

        bool historyLeft = stored.CoveredFrom > from;

        if (stored.CoveredUntil < until && (!historyLeft || until - stored.CoveredUntil >= FreshWhileHistoryLoads))
        {
            return (stored.CoveredUntil, Earlier(stored.CoveredUntil + bybit.PageLength, until));
        }

        if (historyLeft)
        {
            return (Later(stored.CoveredFrom - bybit.PageLength, from), stored.CoveredFrom);
        }

        return null;
    }

    private static LongShortRatioSeries Covered(
        LongShortRatioSeries? stored,
        Guid instrumentId,
        DateTimeOffset from,
        DateTimeOffset until,
        IReadOnlyCollection<LongShortRatioPoint> points)
    {
        if (stored is null)
        {
            return LongShortRatioSeries.Create(instrumentId, from, until, points);
        }

        stored.Cover(from, until, points);

        return stored;
    }

    // The first step of the range: the first one at or after the asked time, and not before the step the contract was launched in.
    private static DateTimeOffset FirstStep(DateTimeOffset asked, BybitListedInstrument listing)
    {
        DateTimeOffset start = LongShortRatioSeries.StartOfStep(asked);
        DateTimeOffset first = start < asked ? start + LongShortRatioSeries.Step : start;

        return listing.LaunchTime is { } launchTime ? Later(first, LongShortRatioSeries.StartOfStep(launchTime)) : first;
    }

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }
}