using System.Runtime.CompilerServices;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Funding;
using ProfitFox.MarketData.Application.Queries.Funding;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding.Queries;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.History;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Funding;

/// <summary>
///     Reads the funding payments of the history a part of the window a statement, <see cref="HistoryPeriod" />, row by row as
///     <see cref="HistoryRows" /> reads them, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetFundingPaymentHistoryQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetFundingPaymentHistoryQuery, InstrumentFundingPaymentInternal>
{
    public async IAsyncEnumerable<InstrumentFundingPaymentInternal> Handle(GetFundingPaymentHistoryQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await HistoryConnection.PrepareAsync(connection, cancellationToken);

        foreach (HistoryPeriod period in HistoryPeriod.Split(request.Coverage))
        {
            IAsyncEnumerable<InstrumentFundingPaymentDb> rows = HistoryRows.ReadAsync<InstrumentFundingPaymentDb>(
                connection,
                GetFundingPaymentHistoryQueries.PointsSql,
                GetFundingPaymentHistoryQueries.Period(period),
                cancellationToken);

            await foreach (InstrumentFundingPaymentDb row in rows)
            {
                yield return new InstrumentFundingPaymentInternal(row.InstrumentId, row.Time, row.Rate);
            }
        }
    }
}