using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding;

/// <summary>
///     Reads the stored points of the window as they are, oldest first, and cuts the answer at <see cref="FundingRateWindow.MaxPoints" />.
/// </summary>
internal sealed class GetFundingRateQueryHandler(IInstrumentRepository instruments, NpgsqlConnection connection)
    : IRequestHandler<GetFundingRateQuery, FundingRatePointsInternal>
{
    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<FundingRatePointsInternal> Handle(GetFundingRateQuery request, CancellationToken cancellationToken)
    {
        // One point over the limit tells that the window holds more, and is where the rest starts.
        FundingRatePointDb[] rows =
        [
            .. await connection.QueryAsync<FundingRatePointDb>(
                GetFundingRateQueries.Points(request.InstrumentId, request.Window, FundingRateWindow.MaxPoints + 1, cancellationToken))
        ];

        // Only an instrument of the catalog has points, so the catalog is asked only when none came.
        if (rows.Length == 0 && await instruments.GetAsync(request.InstrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(request.InstrumentId);
        }

        return new FundingRatePointsInternal(
            [.. rows.Take(FundingRateWindow.MaxPoints).Select(row => new FundingRatePointInternal(row.Time, row.Rate))],
            rows.Length > FundingRateWindow.MaxPoints ? rows[^1].Time : null);
    }
}