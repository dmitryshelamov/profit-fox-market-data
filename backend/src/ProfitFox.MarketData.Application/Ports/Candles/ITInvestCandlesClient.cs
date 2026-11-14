using ProfitFox.MarketData.Domain.ValueObjects.Candles;
using ProfitFox.MarketData.Domain.ValueObjects.Instruments;

namespace ProfitFox.MarketData.Application.Ports.Candles;

/// <summary>
///     The minute candles of the exchange T-Invest gives for its shares; the candles of its dealer trading on weekends are not among them.
/// </summary>
public interface ITInvestCandlesClient
{
    /// <summary>
    ///     Every closed candle of the share that opens inside <paramref name="range" />, at most one day long: one request to T-Invest. Its
    ///     volume is in shares.
    /// </summary>
    Task<IReadOnlyList<Candle>> GetAsync(TInvestListedShare share, MinuteRange range, CancellationToken cancellationToken);

    /// <summary>
    ///     The first moment since <paramref name="since" /> the share may have traded, the time of its first minute candle T-Invest tells;
    ///     null when T-Invest does not tell.
    /// </summary>
    Task<DateTimeOffset?> GetTradingStartAsync(TInvestListedShare share, DateTimeOffset since, CancellationToken cancellationToken);
}