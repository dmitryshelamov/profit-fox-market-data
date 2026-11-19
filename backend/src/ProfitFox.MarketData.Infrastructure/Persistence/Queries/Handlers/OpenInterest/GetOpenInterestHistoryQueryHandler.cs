using System.Runtime.CompilerServices;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

/// <summary>
///     Reads the points of open interest of the history a part of the window a statement, <see cref="HistoryPeriod" />, row by row as
///     <see cref="HistoryRows" /> reads them, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetOpenInterestHistoryQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetOpenInterestHistoryQuery, InstrumentOpenInterestInternal>
{
    public async IAsyncEnumerable<InstrumentOpenInterestInternal> Handle(GetOpenInterestHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await HistoryConnection.PrepareAsync(connection, cancellationToken);

        foreach (HistoryPeriod period in HistoryPeriod.Split(request.Coverage))
        {
            IAsyncEnumerable<InstrumentOpenInterestDb> rows = HistoryRows.ReadAsync<InstrumentOpenInterestDb>(
                connection,
                GetOpenInterestHistoryQueries.PointsSql,
                GetOpenInterestHistoryQueries.Period(period),
                cancellationToken);

            await foreach (InstrumentOpenInterestDb row in rows)
            {
                yield return new InstrumentOpenInterestInternal(row.InstrumentId, row.Time, row.Value);
            }
        }
    }
}