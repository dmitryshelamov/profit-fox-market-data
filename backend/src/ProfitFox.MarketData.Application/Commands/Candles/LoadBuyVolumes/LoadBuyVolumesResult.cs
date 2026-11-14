namespace ProfitFox.MarketData.Application.Commands.Candles.LoadBuyVolumes;

/// <summary>
///     What a command left of its range: days that can be settled now, so the command is sent again, or nothing for now.
/// </summary>
public enum LoadBuyVolumesResult
{
    MoreLeft,
    NothingLeft
}