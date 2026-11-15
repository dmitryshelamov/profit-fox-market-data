using MediatR;

using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Application.Commands.Liquidations.SaveLiquidations;

/// <summary>
///     Adds the sums of liquidations heard since the last save to the stored ones of their minutes, all together, and tells those
///     minutes to the feed of liquidations as storage holds them now; a minute of an instrument comes once. A minute is saved many
///     times while it goes on, and once more for a liquidation told after it ended.
/// </summary>
public sealed record SaveLiquidationsCommand(IReadOnlyCollection<LiquidationMinute> Minutes) : IRequest;