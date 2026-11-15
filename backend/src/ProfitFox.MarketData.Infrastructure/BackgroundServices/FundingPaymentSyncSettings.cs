namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     The <c>FundingPaymentSync</c> section of the settings: whether the funding payments of Bybit are loaded, how far back, and
///     how many contracts at a time.
/// </summary>
internal sealed class FundingPaymentSyncSettings
{
    public const string Section = "FundingPaymentSync";

    /// <summary>
    ///     Days of history back from now whose payments every listed perpetual contract loads.
    /// </summary>
    public int HistoryDays { get; init; } = 365;

    /// <summary>
    ///     How many contracts are asked for at the same time. One after another a round of the whole market lasts minutes, as a request
    ///     takes a third of a second; the requests still share the limit of Bybit with every other one of the app.
    /// </summary>
    public int Parallelism { get; init; } = 4;

    public ExchangeSyncSettings Bybit { get; init; } = new ExchangeSyncSettings();
}