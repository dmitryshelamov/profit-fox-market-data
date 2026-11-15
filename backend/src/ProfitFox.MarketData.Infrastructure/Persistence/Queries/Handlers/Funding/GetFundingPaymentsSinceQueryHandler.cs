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
///     Reads the stored funding payments row by row as they are asked for, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetFundingPaymentsSinceQueryHandler(NpgsqlConnection connection)
    : IStreamRequestHandler<GetFundingPaymentsSinceQuery, InstrumentFundingPaymentInternal>
{
    public async IAsyncEnumerable<InstrumentFundingPaymentInternal> Handle(
        GetFundingPaymentsSinceQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<InstrumentFundingPaymentDb> rows = connection.QueryUnbufferedAsync<InstrumentFundingPaymentDb>(
            GetFundingPaymentsSinceQueries.SinceSql,
            GetFundingPaymentsSinceQueries.Since(request.Since));

        await foreach (InstrumentFundingPaymentDb row in rows.WithCancellation(cancellationToken))
        {
            yield return new InstrumentFundingPaymentInternal(row.InstrumentId, row.Time, row.Rate);
        }
    }
}