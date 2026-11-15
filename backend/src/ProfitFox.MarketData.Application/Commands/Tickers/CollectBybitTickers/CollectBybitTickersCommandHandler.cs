using MediatR;

using ProfitFox.MarketData.Application.Ports.Tickers;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Entities.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;

/// <summary>
///     Asks Bybit first, so the snapshot is as close to its boundary as the request allows, then matches the tickers to the listed
///     linear contracts of the catalog by symbol. A symbol the catalog does not have is left out: the sync of the catalog brings it, and
///     its points start from the snapshot after that. A snapshot is of a time where a step of the open interest series starts,
///     the step its history at the exchange has, so a collected point and a loaded one are of the same time. Each series is saved by
///     one call of its own repository: the two are read apart, so a step whose funding rates were not saved keeps its open interest.
/// </summary>
internal sealed class CollectBybitTickersCommandHandler(
    IBybitTickersClient bybit,
    IInstrumentRepository instruments,
    IOpenInterestRepository openInterest,
    IFundingRateRepository fundingRates) : IRequestHandler<CollectBybitTickersCommand, CollectBybitTickersResult>
{
    /// <exception cref="ArgumentException">No step of the open interest series starts at the time of the snapshot.</exception>
    public async Task<CollectBybitTickersResult> Handle(CollectBybitTickersCommand request, CancellationToken cancellationToken)
    {
        if (!OpenInterestSeries.StartsStep(request.Time))
        {
            throw new ArgumentException($"A snapshot is taken where a step starts; {request.Time:O} is between two steps.", nameof(request));
        }

        IReadOnlyList<BybitLinearTicker> tickers = await bybit.GetLinearAsync(cancellationToken);
        IReadOnlyList<Instrument> stored = await instruments.GetByExchangeAsync(Exchange.Bybit, cancellationToken);

        var tickersBySymbol = tickers.ToDictionary(ticker => ticker.Symbol);
        var openInterestPoints = new List<OpenInterestPoint>();
        var fundingRatePoints = new List<FundingRatePoint>();

        foreach (Instrument instrument in stored.Where(instrument => instrument.DelistedAt is null))
        {
            var listing = (BybitListedInstrument)instrument.Listing;

            // A symbol is unique only within its category: the spot BTCUSDT is not the contract the ticker is of.
            if (listing.Category != BybitCategory.Linear || !tickersBySymbol.TryGetValue(listing.Symbol, out BybitLinearTicker? ticker))
            {
                continue;
            }

            if (ticker.OpenInterest is { } interest)
            {
                openInterestPoints.Add(new OpenInterestPoint(instrument.Id, request.Time, interest));
            }

            if (ticker.FundingRate is { } rate)
            {
                fundingRatePoints.Add(new FundingRatePoint(instrument.Id, request.Time, rate));
            }
        }

        if (openInterestPoints.Count > 0)
        {
            await openInterest.SaveAsync(openInterestPoints, cancellationToken);
        }

        if (fundingRatePoints.Count > 0)
        {
            await fundingRates.SaveAsync(fundingRatePoints, cancellationToken);
        }

        return new CollectBybitTickersResult(openInterestPoints.Count, fundingRatePoints.Count);
    }
}