using System.Runtime.CompilerServices;

using Dapper;

using MediatR;

using Npgsql;

using ProfitFox.MarketData.Application.Contracts.Candles;
using ProfitFox.MarketData.Application.Queries.Candles;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Contracts;
using ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles.Queries;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Queries.Handlers.Candles;

/// <summary>
///     Reads the stored candles row by row as they are asked for, so the caller passes them on without holding them all.
/// </summary>
internal sealed class GetClosedCandlesQueryHandler(NpgsqlConnection connection) : IStreamRequestHandler<GetClosedCandlesQuery, ClosedCandleInternal>
{
    public async IAsyncEnumerable<ClosedCandleInternal> Handle(GetClosedCandlesQuery request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<ClosedCandleDb> rows = connection.QueryUnbufferedAsync<ClosedCandleDb>(
            GetClosedCandlesQueries.SinceSql,
            GetClosedCandlesQueries.Since(request.Since, request.Exchanges));

        await foreach (ClosedCandleDb row in rows.WithCancellation(cancellationToken))
        {
            yield return new ClosedCandleInternal(
                row.Exchange,
                row.InstrumentId,
                row.OpenTime,
                row.Open,
                row.High,
                row.Low,
                row.Close,
                row.Volume,
                row.Turnover,
                row.BuyVolume);
        }
    }
}