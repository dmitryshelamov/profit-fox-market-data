namespace ProfitFox.MarketData.Infrastructure.BackgroundServices;

/// <summary>
///     What a round of a loading left: pages still to load, so another round follows; nothing; or an exchange that is unavailable, so
///     the loading stops for now.
/// </summary>
internal enum LoadingRoundResult
{
    MoreLeft,
    NothingLeft,
    Unavailable
}