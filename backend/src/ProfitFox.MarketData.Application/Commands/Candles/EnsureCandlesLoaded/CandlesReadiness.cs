namespace ProfitFox.MarketData.Application.Commands.Candles.EnsureCandlesLoaded;

/// <summary>
///     Whether storage holds every closed minute of a range now, or the range is still loading in the background.
/// </summary>
public enum CandlesReadiness
{
    Ready,
    Loading
}