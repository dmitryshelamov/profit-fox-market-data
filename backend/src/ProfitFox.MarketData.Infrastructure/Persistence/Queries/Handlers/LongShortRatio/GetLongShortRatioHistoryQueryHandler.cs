using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

/// <summary>
///     Reads the points of the long/short ratio of the history a UTC day a statement, row by row as they are asked for, so the caller passes them on without
///     holding them all.
/// </summary>
internal sealed class GetLongShortRatioHistoryQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetLongShortRatioHistoryQuery, InstrumentLongShortRatioInternal>
{
    public async IAsyncEnumerable<InstrumentLongShortRatioInternal> Handle(GetLongShortRatioHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await HistoryConnection.PrepareAsync(connection, cancellationToken);

        foreach (HistoryDay day in HistoryDay.Split(request.Coverage))
        {
            IAsyncEnumerable<InstrumentLongShortRatioDb> rows = connection.QueryUnbufferedAsync<InstrumentLongShortRatioDb>(GetLongShortRatioHistoryQueries.PointsSql, GetLongShortRatioHistoryQueries.Day(day));

            await foreach (InstrumentLongShortRatioDb row in rows.WithCancellation(cancellationToken))
            {
                yield return new InstrumentLongShortRatioInternal(row.InstrumentId, row.Time, row.Long, row.Short);
            }
        }
    }
}