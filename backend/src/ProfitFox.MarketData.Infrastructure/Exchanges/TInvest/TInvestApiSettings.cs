namespace ProfitFox.MarketData.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The <c>TInvestApi</c> section of the settings: where the app reaches the Invest API of T-Invest, the token it goes with and how it names
///     itself. The token is a secret: the repository leaves it empty, and it comes from the environment or the user secrets.
/// </summary>
internal sealed class TInvestApiSettings
{
    public const string Section = "TInvestApi";

    public string Endpoint { get; init; } = "https://invest-public-api.tbank.ru:443";

    /// <summary>
    ///     The token of a T-Invest account; reading the catalog needs no more than a read-only one.
    /// </summary>
    public string Token { get; init; } = "";

    /// <summary>
    ///     The <c>x-app-name</c> header of every call.
    /// </summary>
    public string AppName { get; init; } = "profit-fox-market-data";

    public bool HasToken => !string.IsNullOrWhiteSpace(Token);
}