using MediatR;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Contracts.Funding.Converters;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Application.Ports.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;

/// <summary>
///     Loads the next uncovered page of the payments of a perpetual contract of Bybit; an instrument that pays no funding (a spot pair, a
///     dated futures contract) has nothing to load. Bybit gives the newest payments of a range, so a full page covers only the time from
///     its oldest payment on, and the rest of the range is asked for by the next command. The payments made since the coverage ended
///     are told to the live feed once saved; a page of history is not: a first load would tell a year of the whole market.
/// </summary>
internal sealed class LoadFundingPaymentsCommandHandler(
    IInstrumentRepository instruments,
    IFundingPaymentSeriesRepository series,
    IBybitFundingPaymentsClient bybit,
    ILiveFeed<InstrumentFundingPaymentInternal> feed) : IRequestHandler<LoadFundingPaymentsCommand, LoadFundingPaymentsResult>
{
    // Bybit tells a payment a few seconds after it is made; a time covered before that would lose the payment for good. The same wait,
    // for the same reason, as the Settling of LoadCandlesCommandHandler. The loading that sends the command must ask later after a
    // whole hour than this (the AfterTheHour of FundingPaymentSyncBackgroundService), or the payment of the hour waits for the next one.
    private static readonly TimeSpan Publishing = TimeSpan.FromSeconds(15);

    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    /// <exception cref="NotSupportedException">The instrument is not of Bybit.</exception>
    /// <exception cref="InvalidOperationException">Bybit gave a full page with no payment of the range, which tells nothing of it.</exception>
    public async Task<LoadFundingPaymentsResult> Handle(LoadFundingPaymentsCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = await instruments.GetAsync(request.InstrumentId, cancellationToken)
            ?? throw new InstrumentNotFoundException(request.InstrumentId);

        if (instrument.Listing.Exchange != Exchange.Bybit)
        {
            throw new NotSupportedException($"Funding payments of {instrument.Listing.Exchange} are not loaded.");
        }

        var listing = (BybitListedInstrument)instrument.Listing;
        // The clock is finer than the microseconds the database keeps and the milliseconds of Bybit: an end of a whole millisecond comes back
        // from storage as it was saved, so the next load finds the coverage up to it and asks no page of less than a millisecond.
        DateTimeOffset requestedUntil = request.RequestedAt - Publishing;
        DateTimeOffset until = requestedUntil.AddTicks(-(requestedUntil.Ticks % TimeSpan.TicksPerMillisecond));

        if (listing.ContractType != BybitContractType.LinearPerpetual || until <= request.From)
        {
            return LoadFundingPaymentsResult.NothingLeft;
        }

        FundingPaymentSeries? stored = await series.GetAsync(instrument.Id, cancellationToken);

        if (Page(stored, request.From, until) is not { } range)
        {
            return LoadFundingPaymentsResult.NothingLeft;
        }

        // Asked before the series takes the page: it grows by it.
        bool paidSinceCoverage = stored is not null && range.From >= stored.CoveredUntil;
        BybitFundingPaymentsPage page = await bybit.GetAsync(listing, range.From, range.Until, cancellationToken);
        FundingPaymentSeries current = Covered(stored, instrument.Id, CoveredFrom(page, range.From, listing), range.Until, page);

        await series.SaveAsync(current, cancellationToken);

        if (paidSinceCoverage && page.Payments.Count > 0)
        {
            feed.Publish([.. page.Payments.Select(payment => payment.ToInternal(instrument.Id))]);
        }

        return current.CoveredFrom > request.From ? LoadFundingPaymentsResult.MoreLeft : LoadFundingPaymentsResult.NothingLeft;
    }

    // The next page to ask for: the whole range for a new series; else what was paid since the coverage ended, in one page up to now;
    // else the history before the coverage. None when the series covers the range.
    private static (DateTimeOffset From, DateTimeOffset Until)? Page(FundingPaymentSeries? stored, DateTimeOffset from, DateTimeOffset until)
    {
        if (stored is null)
        {
            return (from, until);
        }

        if (stored.CoveredUntil < until)
        {
            return (stored.CoveredUntil, until);
        }

        return stored.CoveredFrom > from ? (from, stored.CoveredFrom) : null;
    }

    // Where the page is whole from: a page that is not full holds every payment of its range; a full one may not, so only the time
    // from its oldest payment on is known.
    private static DateTimeOffset CoveredFrom(BybitFundingPaymentsPage page, DateTimeOffset pageFrom, BybitListedInstrument listing)
    {
        if (!page.Full)
        {
            return pageFrom;
        }

        return page.Payments.Count > 0
            ? page.Payments.Min(payment => payment.Time)
            : throw new InvalidOperationException($"Bybit gave a full page of funding payments of {listing.Symbol} with none of the range asked for.");
    }

    private static FundingPaymentSeries Covered(
        FundingPaymentSeries? stored,
        Guid instrumentId,
        DateTimeOffset from,
        DateTimeOffset until,
        BybitFundingPaymentsPage page)
    {
        if (stored is null)
        {
            return FundingPaymentSeries.Create(instrumentId, from, until, page.Payments);
        }

        stored.Cover(from, until, page.Payments);

        return stored;
    }
}