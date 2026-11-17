using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding;

/// <summary>
///     Reads the funding payments of the history a UTC day a statement, row by row as they are asked for, so the caller passes them on without
///     holding them all.
/// </summary>
internal sealed class GetFundingPaymentHistoryQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetFundingPaymentHistoryQuery, InstrumentFundingPaymentInternal>
{
    public async IAsyncEnumerable<InstrumentFundingPaymentInternal> Handle(GetFundingPaymentHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await HistoryConnection.PrepareAsync(connection, cancellationToken);

        foreach (HistoryDay day in HistoryDay.Split(request.Coverage))
        {
            IAsyncEnumerable<InstrumentFundingPaymentDb> rows = connection.QueryUnbufferedAsync<InstrumentFundingPaymentDb>(GetFundingPaymentHistoryQueries.PointsSql, GetFundingPaymentHistoryQueries.Day(day));

            await foreach (InstrumentFundingPaymentDb row in rows.WithCancellation(cancellationToken))
            {
                yield return new InstrumentFundingPaymentInternal(row.InstrumentId, row.Time, row.Rate);
            }
        }
    }
}