using MediatR;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Contracts.Liquidations.Converters;
using ProfitFox.MarketData.Application.Ports.Feeds;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Domain.ValueObjects.Liquidations;

namespace ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;

/// <summary>
///     Marks the minutes a span at a time, each span by a call of its own: an hour of the whole market is tens of thousands of rows,
///     and a downtime of days more than a million, more than one statement should write and give back. A span that fails leaves
///     the ones before it marked; marking again changes nothing, so the caller repeats the whole command. The minutes a span
///     changed are told to the feed once the span is saved; one that was not valid already was told so before and is not told again.
/// </summary>
internal sealed class InvalidateLiquidationsCommandHandler(ILiquidationRepository liquidations, ILiveFeed<InstrumentLiquidationMinuteInternal> feed)
    : IRequestHandler<InvalidateLiquidationsCommand>
{
    private static readonly TimeSpan Span = TimeSpan.FromHours(1);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public async Task Handle(InvalidateLiquidationsCommand request, CancellationToken cancellationToken)
    {
        for (DateTimeOffset from = request.From; from <= request.Until; from += Span)
        {
            // The last minute of the span: both ends are marked.
            DateTimeOffset until = from + Span - Minute;

            IReadOnlyList<StoredLiquidationMinute> marked = await liquidations.InvalidateAsync(
                request.InstrumentIds,
                from,
                until < request.Until ? until : request.Until,
                cancellationToken);

            feed.Publish([.. marked.Select(minute => minute.ToInternal())]);
        }
    }
}