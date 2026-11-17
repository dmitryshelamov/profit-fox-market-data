using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

/// <summary>
///     Reads the points of open interest of the history a UTC day a statement, row by row as they are asked for, so the caller passes them on without
///     holding them all.
/// </summary>
internal sealed class GetOpenInterestHistoryQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetOpenInterestHistoryQuery, InstrumentOpenInterestInternal>
{
    public async IAsyncEnumerable<InstrumentOpenInterestInternal> Handle(GetOpenInterestHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await HistoryConnection.PrepareAsync(connection, cancellationToken);

        foreach (HistoryDay day in HistoryDay.Split(request.Coverage))
        {
            IAsyncEnumerable<InstrumentOpenInterestDb> rows = connection.QueryUnbufferedAsync<InstrumentOpenInterestDb>(GetOpenInterestHistoryQueries.PointsSql, GetOpenInterestHistoryQueries.Day(day));

            await foreach (InstrumentOpenInterestDb row in rows.WithCancellation(cancellationToken))
            {
                yield return new InstrumentOpenInterestInternal(row.InstrumentId, row.Time, row.Value);
            }
        }
    }
}