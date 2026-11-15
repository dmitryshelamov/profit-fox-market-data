using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.LongShortRatio;
using ProfitFox.MarketData.Application.Queries.LongShortRatio;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.LongShortRatio;

/// <summary>
///     Reads the stored points of the long/short ratio row by row as they are asked for, so the caller passes them on without holding
///     them all.
/// </summary>
internal sealed class GetLongShortRatioSinceQueryHandler(NpgsqlConnection connection)
    : IStreamRequestHandler<GetLongShortRatioSinceQuery, InstrumentLongShortRatioInternal>
{
    public async IAsyncEnumerable<InstrumentLongShortRatioInternal> Handle(
        GetLongShortRatioSinceQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<InstrumentLongShortRatioDb> rows = connection.QueryUnbufferedAsync<InstrumentLongShortRatioDb>(
            GetLongShortRatioSinceQueries.SinceSql,
            GetLongShortRatioSinceQueries.Since(request.Since));

        await foreach (InstrumentLongShortRatioDb row in rows.WithCancellation(cancellationToken))
        {
            yield return new InstrumentLongShortRatioInternal(row.InstrumentId, row.Time, row.Long, row.Short);
        }
    }
}