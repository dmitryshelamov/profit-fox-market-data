using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

/// <summary>
///     Reads the coverage of a read of the history of points of open interest in one statement: the series of every instrument it reads, cut to the
///     window.
/// </summary>
internal sealed class GetOpenInterestHistoryCoverageQueryHandler(NpgsqlConnection connection)
    : IRequestHandler<GetOpenInterestHistoryCoverageQuery, IReadOnlyList<InstrumentCoverageInternal>>
{
    public Task<IReadOnlyList<InstrumentCoverageInternal>> Handle(GetOpenInterestHistoryCoverageQuery request, CancellationToken cancellationToken)
    {
        return HistoryCoverageReader.ReadAsync(
            connection,
            GetOpenInterestHistoryQueries.CoverageSql,
            request.InstrumentIds,
            request.Exchanges,
            request.Window,
            cancellationToken);
    }
}