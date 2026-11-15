using MediatR;

using ProfitFox.MarketData.Domain.Repositories;

namespace ProfitFox.MarketData.Application.Commands.Liquidations.InvalidateLiquidations;

/// <summary>
///     Marks the minutes a span at a time, each span by a call of its own: a downtime of days is hundreds of thousands of rows for
///     the whole market, more than one statement should write. A span that fails leaves the ones before it marked; marking again
///     changes nothing, so the caller repeats the whole command.
/// </summary>
internal sealed class InvalidateLiquidationsCommandHandler(ILiquidationRepository liquidations) : IRequestHandler<InvalidateLiquidationsCommand>
{
    private static readonly TimeSpan Span = TimeSpan.FromHours(6);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    public async Task Handle(InvalidateLiquidationsCommand request, CancellationToken cancellationToken)
    {
        for (DateTimeOffset from = request.From; from <= request.Until; from += Span)
        {
            // The last minute of the span: both ends are marked.
            DateTimeOffset until = from + Span - Minute;

            await liquidations.InvalidateAsync(request.InstrumentIds, from, until < request.Until ? until : request.Until, cancellationToken);
        }
    }
}