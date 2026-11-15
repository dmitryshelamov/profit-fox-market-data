using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Exceptions;
using ProfitFox.MarketData.Application.Queries;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Domain.Entities.Funding;
using ProfitFox.MarketData.Domain.Repositories;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding;

/// <summary>
///     Reads the stored payments of the window as they are, oldest first, cuts the answer at <see cref="PointWindow.MaxPoints" />, and
///     tells the coverage of the series beside them.
/// </summary>
internal sealed class GetFundingPaymentsQueryHandler(
    IInstrumentRepository instruments,
    IFundingPaymentSeriesRepository series,
    NpgsqlConnection connection) : IRequestHandler<GetFundingPaymentsQuery, FundingPaymentsInternal>
{
    /// <exception cref="InstrumentNotFoundException">The catalog has no instrument of the id.</exception>
    public async Task<FundingPaymentsInternal> Handle(GetFundingPaymentsQuery request, CancellationToken cancellationToken)
    {
        // The coverage is read before the payments, for the reason GetOpenInterestQueryHandler gives.
        FundingPaymentSeries? stored = await series.GetAsync(request.InstrumentId, cancellationToken);

        // Only an instrument of the catalog has a series, so the catalog is asked only when there is none.
        if (stored is null && await instruments.GetAsync(request.InstrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(request.InstrumentId);
        }

        // One payment over the limit tells that the window holds more, and is where the rest starts.
        FundingPaymentDb[] rows =
        [
            .. await connection.QueryAsync<FundingPaymentDb>(
                GetFundingPaymentsQueries.Payments(request.InstrumentId, request.Window, PointWindow.MaxPoints + 1, cancellationToken))
        ];

        return new FundingPaymentsInternal(
            [.. rows.Take(PointWindow.MaxPoints).Select(row => new FundingPaymentInternal(row.Time, row.Rate))],
            stored?.CoveredFrom,
            stored?.CoveredUntil,
            rows.Length > PointWindow.MaxPoints ? rows[^1].Time : null);
    }
}