namespace ProfitFox.MarketData.Application.Commands.Funding.LoadFundingPayments;

/// <summary>
///     What a page left of its range: history still to load, so the command is sent again, or nothing.
/// </summary>
public enum LoadFundingPaymentsResult
{
    MoreLeft,
    NothingLeft
}