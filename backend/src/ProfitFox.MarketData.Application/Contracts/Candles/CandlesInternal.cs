namespace ProfitFox.MarketData.Application.Contracts.Candles;

/// <summary>
///     The candles of a window, oldest first, each one only when storage holds all its minutes; <see cref="Covered" /> are the parts of the
///     window storage holds, where a minute without a candle had no trades. While <see cref="Loading" />, the window is still being
///     loaded from the exchange and no candles come.
/// </summary>
public sealed record CandlesInternal(bool Loading, IReadOnlyList<CandleInternal> Candles, IReadOnlyList<MinuteRangeInternal> Covered);