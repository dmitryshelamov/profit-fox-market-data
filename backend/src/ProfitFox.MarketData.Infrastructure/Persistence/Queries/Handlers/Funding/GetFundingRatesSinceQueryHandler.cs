using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding;

/// <summary>
///     Reads the stored funding rates row by row as they are asked for, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetFundingRatesSinceQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetFundingRatesSinceQuery, InstrumentFundingRateInternal>
{
    public async IAsyncEnumerable<InstrumentFundingRateInternal> Handle(GetFundingRatesSinceQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<InstrumentFundingRateDb> rows = connection.QueryUnbufferedAsync<InstrumentFundingRateDb>(
            GetFundingRatesSinceQueries.SinceSql,
            GetFundingRatesSinceQueries.Since(request.Since));

        await foreach (InstrumentFundingRateDb row in rows.WithCancellation(cancellationToken))
        {
            yield return new InstrumentFundingRateInternal(row.InstrumentId, row.Time, row.Rate);
        }
    }
}