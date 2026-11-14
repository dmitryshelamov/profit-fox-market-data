using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

/// <summary>
///     Reads the stored points of the window as they are, oldest first, and cuts the answer at <see cref="OpenInterestWindow.MaxPoints" />.
/// </summary>
internal sealed class GetOpenInterestQueryHandler(IInstrumentRepository instruments, NpgsqlConnection connection)
    : IRequestHandler<GetOpenInterestQuery, OpenInterestPointsInternal>
{
    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<OpenInterestPointsInternal> Handle(GetOpenInterestQuery request, CancellationToken cancellationToken)
    {
        // One point over the limit tells that the window holds more, and is where the rest starts.
        OpenInterestPointDb[] rows =
        [
            .. await connection.QueryAsync<OpenInterestPointDb>(
                GetOpenInterestQueries.Points(request.InstrumentId, request.Window, OpenInterestWindow.MaxPoints + 1, cancellationToken))
        ];

        // Only an instrument of the catalog has points, so the catalog is asked only when none came.
        if (rows.Length == 0 && await instruments.GetAsync(request.InstrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(request.InstrumentId);
        }

        return new OpenInterestPointsInternal(
            [.. rows.Take(OpenInterestWindow.MaxPoints).Select(row => new OpenInterestPointInternal(row.Time, row.Value))],
            rows.Length > OpenInterestWindow.MaxPoints ? rows[^1].Time : null);
    }
}