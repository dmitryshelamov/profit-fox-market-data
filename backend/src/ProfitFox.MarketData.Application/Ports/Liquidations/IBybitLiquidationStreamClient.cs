using ProfitFox.MarketData.Application.Exceptions;

namespace ProfitFox.MarketData.Application.Ports.Liquidations;

/// <summary>
///     The liquidations of the linear contracts of Bybit as they happen. Bybit tells them only in its stream and keeps no history of
///     them: what the stream did not tell is never known.
/// </summary>
public interface IBybitLiquidationStreamClient
{
    /// <summary>
    ///     Subscribes to the linear contracts with <paramref name="symbols" />. From then on <paramref name="liquidated" /> gets every
    ///     liquidation of them Bybit tells, until the subscription is disposed. The connection is restored by the client itself:
    ///     <paramref name="lost" /> gets the symbols whose liquidations are not heard from that moment on, a while after the
    ///     connection really broke, and <paramref name="restored" /> gets them once they are heard again.
    /// </summary>
    /// <exception cref="TransientExchangeException">Bybit cannot be reached or refused the subscription; nothing stays subscribed.</exception>
    Task<IAsyncDisposable> SubscribeAsync(
        IReadOnlyCollection<string> symbols,
        Action<BybitLinearLiquidation> liquidated,
        Action<IReadOnlyCollection<string>> lost,
        Action<IReadOnlyCollection<string>> restored,
        CancellationToken cancellationToken);
}