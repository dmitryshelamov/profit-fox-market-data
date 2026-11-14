using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The minute candles Bybit gives for its instruments.
/// </summary>
public interface IBybitCandles
{
    /// <summary>
    ///     Every closed candle of the instrument that opens inside <paramref name="range" />, at most 1000 minutes long: one request to Bybit.
    ///     The candle of a minute that is still open is left out.
    /// </summary>
    Task<IReadOnlyList<Candle>> GetAsync(BybitListedInstrument listing, MinuteRange range, CancellationToken cancellationToken);

    /// <summary>
    ///     The first moment since <paramref name="since" /> the instrument may have traded: its launch time on linear, the day of its first
    ///     daily candle on spot; null when Bybit does not tell or the instrument had no trades since then.
    /// </summary>
    Task<DateTimeOffset?> GetTradingStartAsync(BybitListedInstrument listing, DateTimeOffset since, CancellationToken cancellationToken);
}