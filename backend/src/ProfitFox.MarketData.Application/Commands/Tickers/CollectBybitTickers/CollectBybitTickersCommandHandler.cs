using MediatR;

using ProfitFox.MarketData.Application.Ports.Tickers;
using ProfitFox.MarketData.Application.Ports.Transactions;
using ProfitFox.MarketData.Domain.Entities.Instruments;
using ProfitFox.MarketData.Domain.Entities.Instruments.Enums;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Funding;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;
using ProfitFox.MarketData.Domain.ValueObjects.OpenInterest;

namespace ProfitFox.MarketData.Application.Commands.Tickers.CollectBybitTickers;

/// <summary>
///     Asks Bybit first, so the snapshot is as close to its minute boundary as the request allows, then matches the tickers to the listed
///     linear contracts of the catalog by symbol. A symbol the catalog does not have is left out: the sync of the catalog brings it, and
///     its points start from the snapshot after that.
/// </summary>
internal sealed class CollectBybitTickersCommandHandler(
    IBybitTickersClient bybit,
    IInstrumentRepository instruments,
    IOpenInterestRepository openInterest,
    IFundingRateRepository fundingRates,
    ITransactionExecutor transactions) : IRequestHandler<CollectBybitTickersCommand, CollectBybitTickersResult>
{
    public async Task<CollectBybitTickersResult> Handle(CollectBybitTickersCommand request, CancellationToken cancellationToken)
    {
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

        if (openInterestPoints.Count == 0 && fundingRatePoints.Count == 0)
        {
            return new CollectBybitTickersResult(OpenInterestPoints: 0, FundingRatePoints: 0);
        }

        // One snapshot, two series: a reader never sees the open interest of a minute without its funding rates.
        await transactions.ExecuteAsync(
            async () =>
            {
                await openInterest.SaveAsync(openInterestPoints, cancellationToken);
                await fundingRates.SaveAsync(fundingRatePoints, cancellationToken);
            },
            cancellationToken);

        return new CollectBybitTickersResult(openInterestPoints.Count, fundingRatePoints.Count);
    }
}