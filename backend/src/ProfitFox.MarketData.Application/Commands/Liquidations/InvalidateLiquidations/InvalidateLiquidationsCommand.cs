using MediatR;

namespace ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;

/// <summary>
///     Marks every minute of the instruments from <see cref="From" /> to <see cref="Until" />, both included, as not valid: their
///     liquidations were not heard in full, so the stored sums tell only what was heard. A minute nothing is stored of is stored with
///     sums of nothing, or it would read as a minute without liquidations. The minutes the marking changed are told to the feed of
///     liquidations.
/// </summary>
public sealed record InvalidateLiquidationsCommand(IReadOnlyCollection<Guid> InstrumentIds, DateTimeOffset From, DateTimeOffset Until) : IRequest;