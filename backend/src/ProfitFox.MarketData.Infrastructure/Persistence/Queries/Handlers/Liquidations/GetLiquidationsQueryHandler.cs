using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Liquidations;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Liquidations;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Liquidations;

/// <summary>
///     Reads the stored minutes of the window as they are, oldest first, and cuts the answer at <see cref="PointWindow.MaxPoints" />.
/// </summary>
internal sealed class GetLiquidationsQueryHandler(IInstrumentRepository instruments, NpgsqlConnection connection)
    : IRequestHandler<GetLiquidationsQuery, LiquidationMinutesInternal>
{
    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<LiquidationMinutesInternal> Handle(GetLiquidationsQuery request, CancellationToken cancellationToken)
    {
        // One minute over the limit tells that the window holds more, and is where the rest starts.
        LiquidationMinuteDb[] rows =
        [
            .. await connection.QueryAsync<LiquidationMinuteDb>(
                GetLiquidationsQueries.Minutes(request.InstrumentId, request.Window, PointWindow.MaxPoints + 1, cancellationToken))
        ];

        // Only an instrument of the catalog has minutes, so the catalog is asked only when none came.
        if (rows.Length == 0 && await instruments.GetAsync(request.InstrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(request.InstrumentId);
        }

        return new LiquidationMinutesInternal(
            [.. rows.Take(PointWindow.MaxPoints).Select(ToInternal)],
            rows.Length > PointWindow.MaxPoints ? rows[^1].Time : null);
    }

    private static LiquidationMinuteInternal ToInternal(LiquidationMinuteDb row)
    {
        return new LiquidationMinuteInternal(
            row.Time,
            new LiquidationSumInternal(row.LongVolume, row.LongTurnover, row.LongCount),
            new LiquidationSumInternal(row.ShortVolume, row.ShortTurnover, row.ShortCount),
            row.IsValid);
    }
}