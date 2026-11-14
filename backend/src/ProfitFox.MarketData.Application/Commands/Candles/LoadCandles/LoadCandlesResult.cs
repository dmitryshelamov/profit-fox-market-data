namespace ProfitFox.MarketData.Application.Commands.Candles.LoadCandles;

/// <summary>
///     What a page left of its range: minutes still to load, so the command is sent again, or nothing.
/// </summary>
public enum LoadCandlesResult
{
    MoreLeft,
    NothingLeft
}