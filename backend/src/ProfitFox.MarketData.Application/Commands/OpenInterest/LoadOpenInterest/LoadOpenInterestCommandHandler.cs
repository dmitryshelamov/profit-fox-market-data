using MediatR;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.OpenInterest;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Application.Commands.OpenInterest.LoadOpenInterest;

/// <summary>
///     Loads the next uncovered page of the open interest of a linear contract of Bybit; a spot pair has none. The range is cut to whole
///     steps of the series, to the time since the contract was launched, and to the points Bybit has surely told by now.
/// </summary>
internal sealed class LoadOpenInterestCommandHandler(
    IInstrumentRepository instruments,
    IOpenInterestSeriesRepository series,
    IBybitOpenInterestClient bybit) : IRequestHandler<LoadOpenInterestCommand, LoadOpenInterestResult>
{
    // Bybit tells the point of a step a minute or two after the step starts; covered before that, the step would stay without it. The
    // same wait, for the same reason, as the Settling of LoadCandlesCommandHandler and the Publishing of LoadFundingPaymentsCommandHandler.
    private static readonly TimeSpan Publishing = TimeSpan.FromMinutes(3);

    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    /// <exception cref="NotSupportedException">The instrument is not of Bybit.</exception>
    public async Task<LoadOpenInterestResult> Handle(LoadOpenInterestCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = await instruments.GetAsync(request.InstrumentId, cancellationToken)
            ?? throw new InstrumentNotFoundException(request.InstrumentId);

        if (instrument.Listing.Exchange != Exchange.Bybit)
        {
            throw new NotSupportedException($"Open interest of {instrument.Listing.Exchange} is not loaded.");
        }

        var listing = (BybitListedInstrument)instrument.Listing;

        // The step that starts last among those told by now is inside the range, the one after it is not.
        DateTimeOffset until = OpenInterestSeries.StartOfStep(request.RequestedAt - Publishing) + OpenInterestSeries.Step;
        DateTimeOffset from = FirstStep(request.From, listing);

        if (listing.Category != BybitCategory.Linear || until <= from)
        {
            return LoadOpenInterestResult.NothingLeft;
        }

        OpenInterestSeries? stored = await series.GetAsync(instrument.Id, cancellationToken);
        (DateTimeOffset From, DateTimeOffset Until)? page = Page(stored, from, until);

        if (page is not { } range)
        {
            return LoadOpenInterestResult.NothingLeft;
        }

        IReadOnlyCollection<OpenInterestPoint> points = await PointsAsync(instrument.Id, listing, stored, range.From, range.Until, cancellationToken);
        OpenInterestSeries current = Covered(stored, instrument.Id, range.From, range.Until, points);

        await series.SaveAsync(current, cancellationToken);

        return Page(current, from, until) is null ? LoadOpenInterestResult.NothingLeft : LoadOpenInterestResult.MoreLeft;
    }

    // The next page to load: the newest one of the range for a new series; else the oldest one after the coverage, so the coverage
    // stays one stretch; else the newest one before it. None when the series covers the range.
    private (DateTimeOffset From, DateTimeOffset Until)? Page(OpenInterestSeries? stored, DateTimeOffset from, DateTimeOffset until)
    {
        if (stored is null)
        {
            return (Later(until - bybit.PageLength, from), until);
        }

        if (stored.CoveredUntil < until)
        {
            return (stored.CoveredUntil, Earlier(stored.CoveredUntil + bybit.PageLength, until));
        }

        return stored.CoveredFrom > from ? (Later(stored.CoveredFrom - bybit.PageLength, from), stored.CoveredFrom) : null;
    }

    // The points of the page. A page after the coverage whose every step storage already holds, as the collection kept them, needs no
    // request; a page of history, and a page with a step missing, is asked from Bybit.
    private async Task<IReadOnlyCollection<OpenInterestPoint>> PointsAsync(
        Guid instrumentId,
        BybitListedInstrument listing,
        OpenInterestSeries? stored,
        DateTimeOffset from,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        bool afterCoverage = stored is not null && from >= stored.CoveredUntil;

        if (afterCoverage && await series.CountPointsAsync(instrumentId, from, until, cancellationToken) == OpenInterestSeries.StepsIn(from, until))
        {
            return [];
        }

        IReadOnlyList<BybitOpenInterestPoint> told = await bybit.GetAsync(listing, from, until, cancellationToken);

        return [.. told.Select(point => new OpenInterestPoint(instrumentId, point.Time, point.Value))];
    }

    private static OpenInterestSeries Covered(
        OpenInterestSeries? stored,
        Guid instrumentId,
        DateTimeOffset from,
        DateTimeOffset until,
        IReadOnlyCollection<OpenInterestPoint> points)
    {
        if (stored is null)
        {
            return OpenInterestSeries.Create(instrumentId, from, until, points);
        }

        stored.Cover(from, until, points);

        return stored;
    }

    // The first step of the range: the first one at or after the asked time, and not before the step the contract was launched in.
    private static DateTimeOffset FirstStep(DateTimeOffset asked, BybitListedInstrument listing)
    {
        DateTimeOffset start = OpenInterestSeries.StartOfStep(asked);
        DateTimeOffset first = start < asked ? start + OpenInterestSeries.Step : start;

        return listing.LaunchTime is { } launchTime ? Later(first, OpenInterestSeries.StartOfStep(launchTime)) : first;
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