using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.OpenInterest;
using ProfitFox.MarketData.Application.Queries.OpenInterest;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.OpenInterest;

/// <summary>
///     Reads the stored points of open interest row by row as they are asked for, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetOpenInterestSinceQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetOpenInterestSinceQuery, InstrumentOpenInterestInternal>
{
    public async IAsyncEnumerable<InstrumentOpenInterestInternal> Handle(GetOpenInterestSinceQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<InstrumentOpenInterestDb> rows = connection.QueryUnbufferedAsync<InstrumentOpenInterestDb>(
            GetOpenInterestSinceQueries.SinceSql,
            GetOpenInterestSinceQueries.Since(request.Since));

        await foreach (InstrumentOpenInterestDb row in rows.WithCancellation(cancellationToken))
        {
            yield return new InstrumentOpenInterestInternal(row.InstrumentId, row.Time, row.Value);
        }
    }
}