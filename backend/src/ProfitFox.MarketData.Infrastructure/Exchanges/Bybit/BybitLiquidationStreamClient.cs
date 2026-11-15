using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;

using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;

using Microsoft.Extensions.Logging;

using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Ports.Liquidations;

namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The liquidations of the linear contracts of Bybit from the topic <c>allLiquidation</c> of its public stream, which tells every
///     liquidation of a symbol, twice a second. The side Bybit tells is the side of the liquidated position: <c>Buy</c> is a long.
///     CryptoExchange.Net reconnects and subscribes again; Bybit tells nothing of the time in between.
/// </summary>
internal sealed class BybitLiquidationStreamClient(IBybitSocketClient bybit, ILogger<BybitLiquidationStreamClient> logger) : IBybitLiquidationStreamClient
{
    // As the stream of candles lays its topics out; each request has a subscription of its own, which tells of its connection.
    private const int BatchSize = 10;

    public async Task<IAsyncDisposable> SubscribeAsync(
        IReadOnlyCollection<string> symbols,
        Action<BybitLinearLiquidation> liquidated,
        Action<IReadOnlyCollection<string>> lost,
        Action<IReadOnlyCollection<string>> restored,
        CancellationToken cancellationToken)
    {
        var subscription = new Subscription();

        try
        {
            foreach (string[] batch in symbols.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Made with no token: CryptoExchange.Net closes a subscription when the token it was made with is cancelled.
                WebSocketResult<UpdateSubscription> result = await bybit.V5LinearApi.SubscribeToAllLiquidationUpdatesAsync(
                    batch,
                    update => Tell(update.Data, liquidated),
                    CancellationToken.None);

                if (!result.Success)
                {
                    throw new TransientExchangeException($"Bybit did not take a subscription to its liquidations: {result.Error?.Message}");
                }

                result.Data.ConnectionLost += () =>
                {
                    logger.LogWarning("A connection of the liquidations of Bybit is lost: {Count} symbols are not heard, {First} the first.", batch.Length, batch[0]);
                    Tell(batch, lost);
                };
                result.Data.ConnectionRestored += after =>
                {
                    logger.LogInformation(
                        "A connection of the liquidations of Bybit is back after {Lost}: {Count} symbols are heard again, {First} the first.",
                        after,
                        batch.Length,
                        batch[0]);
                    Tell(batch, restored);
                };
                subscription.Add(result.Data);
            }
        }
        catch
        {
            await subscription.DisposeAsync();

            throw;
        }

        return subscription;
    }

    private void Tell(BybitLiquidationUpdate[] update, Action<BybitLinearLiquidation> liquidated)
    {
        foreach (BybitLiquidationUpdate liquidation in update)
        {
            try
            {
                liquidated(
                    new BybitLinearLiquidation(
                        liquidation.Symbol,
                        new DateTimeOffset(DateTime.SpecifyKind(liquidation.UpdateTime, DateTimeKind.Utc)),
                        liquidation.Side == OrderSide.Buy,
                        liquidation.Quantity,
                        liquidation.Price));
            }
            catch (Exception exception)
            {
                // A listener that failed: this liquidation is not counted, the stream goes on.
                logger.LogError(exception, "A liquidation of {Symbol} of the stream of Bybit is dropped.", liquidation.Symbol);
            }
        }
    }

    private void Tell(string[] symbols, Action<IReadOnlyCollection<string>> listener)
    {
        try
        {
            listener(symbols);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A listener of the connections of the liquidations of Bybit failed.");
        }
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly List<UpdateSubscription> _subscriptions = [];

        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(_subscriptions.Select(subscription => subscription.CloseAsync()));
        }

        public void Add(UpdateSubscription subscription)
        {
            _subscriptions.Add(subscription);
        }
    }
}