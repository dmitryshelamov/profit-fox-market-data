using MediatR;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;

/// <summary>
///     Loads the next uncovered page of the payments of a perpetual contract of Bybit; an instrument that pays no funding (a spot pair, a
///     dated futures contract) has nothing to load. Bybit gives the newest payments of a range, so a full page covers only the time from
///     its oldest payment on, and the rest of the range is asked for by the next command.
/// </summary>
internal sealed class LoadFundingPaymentsCommandHandler(
    IInstrumentRepository instruments,
    IFundingPaymentSeriesRepository series,
    IBybitFundingPaymentsClient bybit) : IRequestHandler<LoadFundingPaymentsCommand, LoadFundingPaymentsResult>
{
    // Bybit tells a payment a few seconds after it is made; a time covered before that would lose the payment for good.
    private static readonly TimeSpan Publishing = TimeSpan.FromSeconds(15);

    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    /// <exception cref="NotSupportedException">The instrument is not of Bybit.</exception>
    public async Task<LoadFundingPaymentsResult> Handle(LoadFundingPaymentsCommand request, CancellationToken cancellationToken)
    {
        Instrument instrument = await instruments.GetAsync(request.InstrumentId, cancellationToken)
            ?? throw new InstrumentNotFoundException(request.InstrumentId);

        if (instrument.Listing.Exchange != Exchange.Bybit)
        {
            throw new NotSupportedException($"Funding payments of {instrument.Listing.Exchange} are not loaded.");
        }

        var listing = (BybitListedInstrument)instrument.Listing;
        DateTimeOffset until = request.RequestedAt - Publishing;

        if (listing.ContractType != BybitContractType.LinearPerpetual || until <= request.From)
        {
            return LoadFundingPaymentsResult.NothingLeft;
        }

        FundingPaymentSeries? stored = await series.GetAsync(instrument.Id, cancellationToken);

        // The whole range for a new series; else what was paid since the coverage ended, and only then the history before it.
        DateTimeOffset pageFrom = stored is not null && stored.CoveredUntil < until ? stored.CoveredUntil : request.From;
        DateTimeOffset pageUntil = stored is null || stored.CoveredUntil < until ? until : stored.CoveredFrom;

        if (pageUntil <= pageFrom)
        {
            return LoadFundingPaymentsResult.NothingLeft;
        }

        IReadOnlyList<FundingPayment> payments = await bybit.GetAsync(listing, pageFrom, pageUntil, cancellationToken);

        // A full page may not be all of the range: only the time from its oldest payment on is known to be whole.
        DateTimeOffset coveredFrom = payments.Count < bybit.PageSize ? pageFrom : payments.Min(payment => payment.Time);
        FundingPaymentSeries current = Covered(stored, instrument.Id, coveredFrom, pageUntil, payments);

        await series.SaveAsync(current, cancellationToken);

        return current.CoveredFrom > request.From ? LoadFundingPaymentsResult.MoreLeft : LoadFundingPaymentsResult.NothingLeft;
    }

    private static FundingPaymentSeries Covered(
        FundingPaymentSeries? stored,
        Guid instrumentId,
        DateTimeOffset from,
        DateTimeOffset until,
        IReadOnlyList<FundingPayment> payments)
    {
        if (stored is null)
        {
            return FundingPaymentSeries.Create(instrumentId, from, until, payments);
        }

        stored.Cover(from, until, payments);

        return stored;
    }
}