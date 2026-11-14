using MediatR;

using ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Candles;
using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Application.Commands.Candles.EnsureCandlesLoaded;

/// <summary>
///     Sends <see cref="LoadCandlesCommand" /> page by page, each saved by itself, until nothing of the range is left or the requests the
///     read may wait for are spent.
/// </summary>
internal sealed class EnsureCandlesLoadedCommandHandler(IInstrumentRepository instruments, ISender sender, ICandleLoadQueue queue)
    : IRequestHandler<EnsureCandlesLoadedCommand, CandlesReadiness>
{
    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<CandlesReadiness> Handle(EnsureCandlesLoadedCommand request, CancellationToken cancellationToken)
    {
        if (await instruments.GetAsync(request.InstrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(request.InstrumentId);
        }

        try
        {
            for (var sent = 0; sent < request.MaxExchangeRequests; sent++)
            {
                var page = new LoadCandlesCommand(request.InstrumentId, request.Range, request.RequestedAt);

                if (await sender.Send(page, cancellationToken) == LoadCandlesResult.NothingLeft)
                {
                    return CandlesReadiness.Ready;
                }
            }
        }
        catch (Exception exception) when (exception is ExchangePausedException or TransientExchangeException)
        {
            // The exchange takes no requests for a while: the background loads the range once it does.
        }

        queue.Enqueue(request.InstrumentId, request.Range);

        return CandlesReadiness.Loading;
    }
}