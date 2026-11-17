using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding;

/// <summary>
///     Reads the coverage of a read of the history of funding payments in one statement: the series of every instrument it reads, cut to the
///     window.
/// </summary>
internal sealed class GetFundingPaymentHistoryCoverageQueryHandler(NpgsqlConnection connection)
    : IRequestHandler<GetFundingPaymentHistoryCoverageQuery, IReadOnlyList<InstrumentCoverageInternal>>
{
    public Task<IReadOnlyList<InstrumentCoverageInternal>> Handle(GetFundingPaymentHistoryCoverageQuery request, CancellationToken cancellationToken)
    {
        return HistoryCoverageReader.ReadAsync(
            connection,
            GetFundingPaymentHistoryQueries.CoverageSql,
            request.InstrumentIds,
            request.Exchanges,
            request.Window,
            cancellationToken);
    }
}