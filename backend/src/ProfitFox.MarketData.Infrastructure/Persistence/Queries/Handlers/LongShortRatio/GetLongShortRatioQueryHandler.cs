using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Domain.Entities.LongShortRatio;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

/// <summary>
///     Reads the stored points of the window as they are, oldest first, cuts the answer at <see cref="PointWindow.MaxPoints" />, and
///     tells the coverage of the series beside them.
/// </summary>
internal sealed class GetLongShortRatioQueryHandler(
    IInstrumentRepository instruments,
    ILongShortRatioSeriesRepository series,
    NpgsqlConnection connection) : IRequestHandler<GetLongShortRatioQuery, LongShortRatioPointsInternal>
{
    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<LongShortRatioPointsInternal> Handle(GetLongShortRatioQuery request, CancellationToken cancellationToken)
    {
        // The coverage is read before the points: it only grows, so the points read after it hold every step it tells of. Read
        // after them, it could tell of a step loaded in between that the answer does not have.
        LongShortRatioSeries? stored = await series.GetAsync(request.InstrumentId, cancellationToken);

        // One point over the limit tells that the window holds more, and is where the rest starts.
        LongShortRatioPointDb[] rows =
        [
            .. await connection.QueryAsync<LongShortRatioPointDb>(
                GetLongShortRatioQueries.Points(request.InstrumentId, request.Window, PointWindow.MaxPoints + 1, cancellationToken))
        ];

        // Only an instrument of the catalog has points or a series, so the catalog is asked only when it has neither.
        if (rows.Length == 0 && stored is null && await instruments.GetAsync(request.InstrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(request.InstrumentId);
        }

        return new LongShortRatioPointsInternal(
            [.. rows.Take(PointWindow.MaxPoints).Select(row => new LongShortRatioPointInternal(row.Time, row.Long, row.Short))],
            stored?.CoveredFrom,
            stored?.CoveredUntil,
            rows.Length > PointWindow.MaxPoints ? rows[^1].Time : null);
    }
}