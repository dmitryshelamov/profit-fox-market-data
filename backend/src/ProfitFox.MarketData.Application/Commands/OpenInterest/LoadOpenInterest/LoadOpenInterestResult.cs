namespace ProfitFox.MarketData.Application.Commands.OpenInterest.LoadOpenInterest;

/// <summary>
///     What a page left of its range: more to load, so the command is sent again, or nothing.
/// </summary>
public enum LoadOpenInterestResult
{
    MoreLeft,
    NothingLeft
}