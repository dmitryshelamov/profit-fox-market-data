using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

/// <summary>
///     Reads the coverage of a read of the history of points of the long/short ratio in one statement: the series of every instrument it reads, cut to the
///     window.
/// </summary>
internal sealed class GetLongShortRatioHistoryCoverageQueryHandler(NpgsqlConnection connection)
    : IRequestHandler<GetLongShortRatioHistoryCoverageQuery, IReadOnlyList<InstrumentCoverageInternal>>
{
    public Task<IReadOnlyList<InstrumentCoverageInternal>> Handle(GetLongShortRatioHistoryCoverageQuery request, CancellationToken cancellationToken)
    {
        return HistoryCoverageReader.ReadAsync(
            connection,
            GetLongShortRatioHistoryQueries.CoverageSql,
            request.InstrumentIds,
            request.Exchanges,
            request.Window,
            cancellationToken);
    }
}