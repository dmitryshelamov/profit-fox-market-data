namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles;

/// <summary>
///     The <c>CandleRead</c> section of the settings: how long a read of candles may wait for the minutes storage does not hold yet.
/// </summary>
internal sealed class CandleReadSettings
{
    public const string Section = "CandleRead";

    /// <summary>
    ///     The requests to the exchange one read may make to load the minutes of its window; a window that needs more loads in the
    ///     background, and the read answers that it is loading. 60 requests take about 6 s on Bybit.
    /// </summary>
    public int MaxExchangeRequests { get; init; } = 60;
}