namespace ProfitFox.MarketData.Application.Commands.LongShortRatio.LoadLongShortRatio;

/// <summary>
///     What a page left of its range: more to load, so the command is sent again, or nothing.
/// </summary>
public enum LoadLongShortRatioResult
{
    MoreLeft,
    NothingLeft
}