namespace ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

/// <summary>
///     The <c>BybitApi</c> section of the settings: how fast the app sends requests to Bybit and how long it holds them after Bybit limited
///     them. The limit of Bybit is per IP, which profit-fox shares, so the app keeps well below it.
/// </summary>
internal sealed class BybitApiSettings
{
    public const string Section = "BybitApi";

    public int RequestsPerSecond { get; init; } = 10;

    /// <summary>
    ///     The pause after the first answer about the limit; each next one in a row pauses twice as long, up to <see cref="BanPauseMinutes" />.
    /// </summary>
    public int LimitPauseSeconds { get; init; } = 30;

    /// <summary>
    ///     The pause after Bybit banned the IP (HTTP 403), and the longest pause after its limit.
    /// </summary>
    public int BanPauseMinutes { get; init; } = 10;
}