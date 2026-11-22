using MediatR;

using Microsoft.Extensions.Options;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles;

/// <summary>
///     Reads the coverage of a read of the history of candles in one statement: the series of every instrument it reads, cut to the window.
/// </summary>
internal sealed class GetCandleHistoryCoverageQueryHandler(NpgsqlConnection connection, IOptionsMonitor<HistoryReadSettings> settings)
    : IRequestHandler<GetCandleHistoryCoverageQuery, IReadOnlyList<InstrumentCoverageInternal>>
{
    public Task<IReadOnlyList<InstrumentCoverageInternal>> Handle(GetCandleHistoryCoverageQuery request, CancellationToken cancellationToken)
    {
        return HistoryCoverageReader.ReadAsync(
            connection,
            GetCandleHistoryQueries.CoverageSql,
            request.InstrumentIds,
            request.Exchanges,
            request.Window.Window,
            settings.CurrentValue.StatementTimeoutSeconds,
            cancellationToken);
    }
}